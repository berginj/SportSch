using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class ScheduleFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public ScheduleFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<ScheduleFunctions>();
    }

    public record ScheduleConstraints(int? maxGamesPerWeek, bool? noDoubleHeaders, bool? balanceHomeAway, int? externalOfferPerWeek);
    public record ScheduleRequest(string? division, string? dateFrom, string? dateTo, ScheduleConstraints? constraints);

    public record ScheduleSlotDto(
        string slotId,
        string gameDate,
        string startTime,
        string endTime,
        string fieldKey,
        string homeTeamId,
        string awayTeamId,
        bool isExternalOffer
    );

    public record SchedulePreviewDto(
        object summary,
        List<ScheduleSlotDto> assignments,
        List<ScheduleSlotDto> unassignedSlots,
        List<object> unassignedMatchups
    );

    [Function("SchedulePreview")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/preview")] HttpRequestData req)
    {
        return await RunSchedule(req, apply: false);
    }

    [Function("ScheduleApply")]
    public async Task<HttpResponseData> Apply(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/apply")] HttpRequestData req)
    {
        return await RunSchedule(req, apply: true);
    }

    private async Task<HttpResponseData> RunSchedule(HttpRequestData req, bool apply)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<ScheduleRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division is required");
            ApiGuards.EnsureValidTableKeyPart("division", division);

            var dateFrom = (body.dateFrom ?? "").Trim();
            var dateTo = (body.dateTo ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(dateFrom) && !DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!string.IsNullOrWhiteSpace(dateTo) && !DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");

            var constraints = body.constraints ?? new ScheduleConstraints(null, null, null, null);
            var maxGamesPerWeek = (constraints.maxGamesPerWeek ?? 0) <= 0 ? (int?)null : constraints.maxGamesPerWeek;
            var noDoubleHeaders = constraints.noDoubleHeaders ?? true;
            var balanceHomeAway = constraints.balanceHomeAway ?? true;
            var externalOfferPerWeek = Math.Max(0, constraints.externalOfferPerWeek ?? 0);

            var teams = await LoadTeamsAsync(leagueId, division);
            if (teams.Count < 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Need at least two teams to schedule.");

            var slots = await LoadOpenSlotsAsync(leagueId, division, dateFrom, dateTo);
            if (slots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No open slots found for this division.");

            var matchups = BuildRoundRobin(teams);
            var result = AssignMatchups(slots, matchups, teams, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, externalOfferPerWeek);

            if (apply)
            {
                var runId = Guid.NewGuid().ToString("N");
                await SaveScheduleRunAsync(leagueId, division, runId, me.Email ?? me.UserId, dateFrom, dateTo, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, externalOfferPerWeek, result);
                await ApplyAssignmentsAsync(leagueId, division, runId, result.assignments);
                return ApiResponses.Ok(req, new { runId, result.summary, result.assignments, result.unassignedSlots, result.unassignedMatchups });
            }

            return ApiResponses.Ok(req, new SchedulePreviewDto(result.summary, result.assignments, result.unassignedSlots, result.unassignedMatchups));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Schedule run failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<List<string>> LoadTeamsAsync(string leagueId, string division)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Teams);
        var pk = Constants.Pk.Teams(leagueId, division);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
        var list = new List<string>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var teamId = (e.GetString("TeamId") ?? e.RowKey ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(teamId)) list.Add(teamId);
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    }

    private async Task<List<SlotInfo>> LoadOpenSlotsAsync(string leagueId, string division, string dateFrom, string dateTo)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
        var pk = Constants.Pk.Slots(leagueId, division);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}' and Status eq '{ApiGuards.EscapeOData(Constants.Status.SlotOpen)}'";
        if (!string.IsNullOrWhiteSpace(dateFrom))
            filter += $" and GameDate ge '{ApiGuards.EscapeOData(dateFrom)}'";
        if (!string.IsNullOrWhiteSpace(dateTo))
            filter += $" and GameDate le '{ApiGuards.EscapeOData(dateTo)}'";

        var list = new List<SlotInfo>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var awayTeamId = (e.GetString("AwayTeamId") ?? "").Trim();
            var isExternalOffer = e.GetBoolean("IsExternalOffer") ?? false;
            if (!string.IsNullOrWhiteSpace(awayTeamId) || isExternalOffer) continue;

            list.Add(new SlotInfo(
                slotId: e.RowKey,
                gameDate: (e.GetString("GameDate") ?? "").Trim(),
                startTime: (e.GetString("StartTime") ?? "").Trim(),
                endTime: (e.GetString("EndTime") ?? "").Trim(),
                fieldKey: (e.GetString("FieldKey") ?? "").Trim(),
                offeringTeamId: (e.GetString("OfferingTeamId") ?? "").Trim()
            ));
        }

        return list
            .Where(s => !string.IsNullOrWhiteSpace(s.gameDate))
            .OrderBy(s => s.gameDate)
            .ThenBy(s => s.startTime)
            .ThenBy(s => s.fieldKey)
            .ToList();
    }

    private static List<(string home, string away)> BuildRoundRobin(List<string> teamIds)
    {
        var teams = new List<string>(teamIds);
        if (teams.Count % 2 == 1) teams.Add("BYE");

        var rounds = teams.Count - 1;
        var half = teams.Count / 2;
        var matchups = new List<(string home, string away)>();

        for (var round = 0; round < rounds; round++)
        {
            for (var i = 0; i < half; i++)
            {
                var teamA = teams[i];
                var teamB = teams[teams.Count - 1 - i];
                if (teamA == "BYE" || teamB == "BYE") continue;

                var home = round % 2 == 0 ? teamA : teamB;
                var away = round % 2 == 0 ? teamB : teamA;
                matchups.Add((home, away));
            }

            var last = teams[^1];
            teams.RemoveAt(teams.Count - 1);
            teams.Insert(1, last);
        }

        return matchups;
    }

    private static ScheduleResult AssignMatchups(
        List<SlotInfo> slots,
        List<(string home, string away)> matchups,
        List<string> teams,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        int externalOfferPerWeek)
    {
        var teamSet = new HashSet<string>(teams, StringComparer.OrdinalIgnoreCase);
        var homeCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var awayCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var gamesByDate = teams.ToDictionary(t => t, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var gamesByWeek = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var assignments = new List<ScheduleSlotDto>();
        var remainingMatchups = new List<(string home, string away)>(matchups);
        var unassignedSlots = new List<ScheduleSlotDto>();

        foreach (var slot in slots)
        {
            var fixedHome = teamSet.Contains(slot.offeringTeamId) ? slot.offeringTeamId : "";
            var pick = PickMatchup(slot.gameDate, fixedHome, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway);
            if (pick is null)
            {
                unassignedSlots.Add(new ScheduleSlotDto(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, "", "", false));
                continue;
            }

            var (home, away) = pick.Value;
            remainingMatchups.Remove(pick.Value);
            ApplyCounts(home, away, slot.gameDate, homeCounts, awayCounts, gamesByDate, gamesByWeek);
            assignments.Add(new ScheduleSlotDto(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, home, away, false));
        }

        if (externalOfferPerWeek > 0 && unassignedSlots.Count > 0)
        {
            var remaining = new List<ScheduleSlotDto>();
            var byWeek = unassignedSlots
                .GroupBy(s => WeekKey(s.gameDate))
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in byWeek)
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    remaining.AddRange(group);
                    continue;
                }

                var picks = group.Take(externalOfferPerWeek).ToList();
                foreach (var slot in picks)
                {
                    var home = PickExternalHome(teams, homeCounts, awayCounts);
                    ApplyCounts(home, "", slot.gameDate, homeCounts, awayCounts, gamesByDate, gamesByWeek);
                    assignments.Add(new ScheduleSlotDto(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, home, "", true));
                }

                remaining.AddRange(group.Skip(externalOfferPerWeek));
            }

            unassignedSlots = remaining;
        }

        var unassignedMatchups = remainingMatchups
            .Select(m => (object)new { homeTeamId = m.home, awayTeamId = m.away })
            .ToList();

        var summary = new
        {
            slotsTotal = slots.Count,
            slotsAssigned = assignments.Count,
            matchupsTotal = matchups.Count,
            matchupsAssigned = matchups.Count - remainingMatchups.Count,
            externalOffers = assignments.Count(a => a.isExternalOffer),
            unassignedSlots = unassignedSlots.Count,
            unassignedMatchups = remainingMatchups.Count
        };

        return new ScheduleResult(summary, assignments, unassignedSlots, unassignedMatchups);
    }

    private static (string home, string away)? PickMatchup(
        string gameDate,
        string fixedHome,
        List<(string home, string away)> matchups,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway)
    {
        (string home, string away)? best = null;
        var bestScore = int.MaxValue;

        foreach (var m in matchups)
        {
            var home = m.home;
            var away = m.away;

            if (!string.IsNullOrWhiteSpace(fixedHome))
            {
                if (!string.Equals(home, fixedHome, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(away, fixedHome, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(home, fixedHome, StringComparison.OrdinalIgnoreCase))
                {
                    home = fixedHome;
                    away = m.home;
                }
            }

            if (!CanAssign(home, away, gameDate, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders)) continue;

            var score = 0;
            if (balanceHomeAway)
            {
                var homeDiff = Math.Abs((homeCounts[home] + 1) - awayCounts[home]);
                var awayDiff = Math.Abs((awayCounts[away] + 1) - homeCounts[away]);
                score = homeDiff + awayDiff;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = (home, away);
                if (score == 0) break;
            }
        }

        return best;
    }

    private static bool CanAssign(
        string home,
        string away,
        string gameDate,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders)
    {
        if (noDoubleHeaders)
        {
            if (gamesByDate[home].Contains(gameDate)) return false;
            if (gamesByDate[away].Contains(gameDate)) return false;
        }

        if (maxGamesPerWeek.HasValue)
        {
            var weekKey = WeekKey(gameDate);
            if (!string.IsNullOrWhiteSpace(weekKey))
            {
                if (GetWeekCount(gamesByWeek, home, weekKey) >= maxGamesPerWeek.Value) return false;
                if (GetWeekCount(gamesByWeek, away, weekKey) >= maxGamesPerWeek.Value) return false;
            }
        }

        return true;
    }

    private static void ApplyCounts(
        string home,
        string away,
        string gameDate,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek)
    {
        if (!string.IsNullOrWhiteSpace(home))
        {
            homeCounts[home] += 1;
            gamesByDate[home].Add(gameDate);
            AddWeekCount(gamesByWeek, home, gameDate);
        }
        if (!string.IsNullOrWhiteSpace(away))
        {
            awayCounts[away] += 1;
            gamesByDate[away].Add(gameDate);
            AddWeekCount(gamesByWeek, away, gameDate);
        }
    }

    private static string PickExternalHome(List<string> teams, Dictionary<string, int> homeCounts, Dictionary<string, int> awayCounts)
    {
        return teams
            .OrderBy(t => homeCounts[t] + awayCounts[t])
            .ThenBy(t => homeCounts[t])
            .ThenBy(t => t)
            .First();
    }

    private static string WeekKey(string gameDate)
    {
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return "";
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{dt.Year}-W{week:D2}";
    }

    private static int GetWeekCount(Dictionary<string, int> gamesByWeek, string teamId, string weekKey)
    {
        var key = $"{teamId}|{weekKey}";
        return gamesByWeek.TryGetValue(key, out var v) ? v : 0;
    }

    private static void AddWeekCount(Dictionary<string, int> gamesByWeek, string teamId, string gameDate)
    {
        var weekKey = WeekKey(gameDate);
        if (string.IsNullOrWhiteSpace(weekKey)) return;
        var key = $"{teamId}|{weekKey}";
        gamesByWeek[key] = gamesByWeek.TryGetValue(key, out var v) ? v + 1 : 1;
    }

    private async Task SaveScheduleRunAsync(
        string leagueId,
        string division,
        string runId,
        string createdBy,
        string dateFrom,
        string dateTo,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        int externalOfferPerWeek,
        ScheduleResult result)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.ScheduleRuns);
        var pk = Constants.Pk.ScheduleRuns(leagueId, division);
        var now = DateTimeOffset.UtcNow;
        var entity = new TableEntity(pk, runId)
        {
            ["LeagueId"] = leagueId,
            ["Division"] = division,
            ["RunId"] = runId,
            ["CreatedBy"] = createdBy,
            ["CreatedUtc"] = now,
            ["DateFrom"] = dateFrom,
            ["DateTo"] = dateTo,
            ["MaxGamesPerWeek"] = maxGamesPerWeek ?? 0,
            ["NoDoubleHeaders"] = noDoubleHeaders,
            ["BalanceHomeAway"] = balanceHomeAway,
            ["ExternalOfferPerWeek"] = externalOfferPerWeek,
            ["Summary"] = JsonSerializer.Serialize(result.summary)
        };

        await table.AddEntityAsync(entity);
    }

    private async Task ApplyAssignmentsAsync(string leagueId, string division, string runId, List<ScheduleSlotDto> assignments)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
        var pk = Constants.Pk.Slots(leagueId, division);

        foreach (var a in assignments)
        {
            TableEntity slot;
            try
            {
                slot = (await table.GetEntityAsync<TableEntity>(pk, a.slotId)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                continue;
            }

            slot["OfferingTeamId"] = a.homeTeamId;
            slot["HomeTeamId"] = a.homeTeamId;
            slot["AwayTeamId"] = a.awayTeamId ?? "";
            slot["IsExternalOffer"] = a.isExternalOffer;
            slot["IsAvailability"] = false;
            slot["ScheduleRunId"] = runId;
            slot["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(slot, slot.ETag, TableUpdateMode.Merge);
        }
    }

    private record SlotInfo(string slotId, string gameDate, string startTime, string endTime, string fieldKey, string offeringTeamId);
    private record ScheduleResult(object summary, List<ScheduleSlotDto> assignments, List<ScheduleSlotDto> unassignedSlots, List<object> unassignedMatchups);
}
