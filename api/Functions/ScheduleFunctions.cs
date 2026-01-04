using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Scheduling;
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

    public record ScheduleConstraintsDto(int? maxGamesPerWeek, bool? noDoubleHeaders, bool? balanceHomeAway, int? externalOfferPerWeek);
    public record ScheduleRequest(string? division, string? dateFrom, string? dateTo, ScheduleConstraintsDto? constraints);

    public record SchedulePreviewDto(
        object summary,
        List<ScheduleSlotDto> assignments,
        List<ScheduleSlotDto> unassignedSlots,
        List<object> unassignedMatchups,
        List<object> failures
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

            var constraints = body.constraints ?? new ScheduleConstraintsDto(null, null, null, null);
            var maxGamesPerWeek = (constraints.maxGamesPerWeek ?? 0) <= 0 ? (int?)null : constraints.maxGamesPerWeek;
            var noDoubleHeaders = constraints.noDoubleHeaders ?? true;
            var balanceHomeAway = constraints.balanceHomeAway ?? true;
            var externalOfferPerWeek = Math.Max(0, constraints.externalOfferPerWeek ?? 0);
            var scheduleConstraints = new ScheduleConstraints(maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, externalOfferPerWeek);

            var teams = await LoadTeamsAsync(leagueId, division);
            if (teams.Count < 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Need at least two teams to schedule.");

            var slots = await LoadAvailabilitySlotsAsync(leagueId, division, dateFrom, dateTo);
            if (slots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No availability slots found for this division.");

            var matchups = ScheduleEngine.BuildRoundRobin(teams);
            var scheduleSlots = slots.Select(slot => new ScheduleSlot(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, slot.offeringTeamId)).ToList();
            var result = ScheduleEngine.AssignMatchups(scheduleSlots, matchups, teams, scheduleConstraints);
            var validation = ScheduleValidation.Validate(result, scheduleConstraints);
            var assignments = result.Assignments.Select(ToSlotDto).ToList();
            var unassignedSlots = result.UnassignedSlots.Select(ToSlotDto).ToList();
            var unassignedMatchups = result.UnassignedMatchups
                .Select(m => (object)new { homeTeamId = m.HomeTeamId, awayTeamId = m.AwayTeamId })
                .ToList();
            var failures = validation.Issues
                .Select(i => (object)new { ruleId = i.RuleId, severity = i.Severity, message = i.Message, details = i.Details })
                .ToList();

            if (apply)
            {
                if (validation.TotalIssues > 0)
                {
                    return ApiResponses.Error(
                        req,
                        HttpStatusCode.BadRequest,
                        "SCHEDULE_VALIDATION_FAILED",
                        $"Schedule validation failed with {validation.TotalIssues} issue(s). Review the Schedule preview and adjust constraints, then try again. See /#schedule.");
                }
                var runId = Guid.NewGuid().ToString("N");
                await SaveScheduleRunAsync(leagueId, division, runId, me.Email ?? me.UserId, dateFrom, dateTo, scheduleConstraints, result);
                await ApplyAssignmentsAsync(leagueId, division, runId, result.Assignments);
                return ApiResponses.Ok(req, new { runId, result.Summary, assignments, unassignedSlots, unassignedMatchups, failures });
            }

            return ApiResponses.Ok(req, new SchedulePreviewDto(result.Summary, assignments, unassignedSlots, unassignedMatchups, failures));
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

    private async Task<List<SlotInfo>> LoadAvailabilitySlotsAsync(string leagueId, string division, string dateFrom, string dateTo)
    {
        var expanded = await TryLoadAvailabilityExpansionAsync(leagueId, division, dateFrom, dateTo);
        if (expanded is not null)
            return expanded;

        return await LoadAvailabilitySlotsFromTableAsync(leagueId, division, dateFrom, dateTo);
    }

    private async Task<List<SlotInfo>> LoadAvailabilitySlotsFromTableAsync(string leagueId, string division, string dateFrom, string dateTo)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
        var pk = Constants.Pk.Slots(leagueId, division);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}' and Status eq '{ApiGuards.EscapeOData(Constants.Status.SlotOpen)}' and IsAvailability eq true";
        if (!string.IsNullOrWhiteSpace(dateFrom))
            filter += $" and GameDate ge '{ApiGuards.EscapeOData(dateFrom)}'";
        if (!string.IsNullOrWhiteSpace(dateTo))
            filter += $" and GameDate le '{ApiGuards.EscapeOData(dateTo)}'";

        var list = new List<SlotInfo>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var homeTeamId = (e.GetString("HomeTeamId") ?? "").Trim();
            var awayTeamId = (e.GetString("AwayTeamId") ?? "").Trim();
            var isExternalOffer = e.GetBoolean("IsExternalOffer") ?? false;
            if (!string.IsNullOrWhiteSpace(homeTeamId) || !string.IsNullOrWhiteSpace(awayTeamId) || isExternalOffer) continue;

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

    private async Task<List<SlotInfo>?> TryLoadAvailabilityExpansionAsync(string leagueId, string division, string dateFrom, string dateTo)
    {
        var endpoint = (Environment.GetEnvironmentVariable("GAMESWAP_AVAILABILITY_EXPANSION_URL")
            ?? Environment.GetEnvironmentVariable("AVAILABILITY_EXPANSION_URL")
            ?? "").Trim();
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            _log.LogWarning("Availability expansion endpoint is invalid: {endpoint}", endpoint);
            return null;
        }

        var request = new AvailabilityExpansionRequest(
            leagueId,
            division,
            string.IsNullOrWhiteSpace(dateFrom) ? null : dateFrom,
            string.IsNullOrWhiteSpace(dateTo) ? null : dateTo);

        try
        {
            using var client = new HttpClient();
            using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(uri, content);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Availability expansion service returned {status}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync();
            var expanded = ParseAvailabilitySlots(payload);
            if (expanded.Count == 0) return null;

            var filtered = expanded
                .Where(s => string.IsNullOrWhiteSpace(s.division) || string.Equals(s.division, division, StringComparison.OrdinalIgnoreCase))
                .Where(s => IsWithinDateRange(s.gameDate, dateFrom, dateTo))
                .Select(s => new SlotInfo(
                    slotId: s.slotId,
                    gameDate: s.gameDate,
                    startTime: s.startTime,
                    endTime: s.endTime,
                    fieldKey: s.fieldKey,
                    offeringTeamId: s.offeringTeamId ?? ""))
                .Where(s => !string.IsNullOrWhiteSpace(s.slotId))
                .ToList();

            var ordered = filtered
                .OrderBy(s => s.gameDate)
                .ThenBy(s => s.startTime)
                .ThenBy(s => s.fieldKey)
                .ToList();
            return ordered.Count == 0 ? null : ordered;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Availability expansion service failed");
            return null;
        }
    }

    private static bool IsWithinDateRange(string gameDate, string dateFrom, string dateTo)
    {
        if (string.IsNullOrWhiteSpace(gameDate)) return false;
        if (!string.IsNullOrWhiteSpace(dateFrom) && string.CompareOrdinal(gameDate, dateFrom) < 0) return false;
        if (!string.IsNullOrWhiteSpace(dateTo) && string.CompareOrdinal(gameDate, dateTo) > 0) return false;
        return true;
    }

    private static List<AvailabilitySlotDto> ParseAvailabilitySlots(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return new List<AvailabilitySlotDto>();

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("slots", out var slotsElement))
            {
                root = slotsElement;
            }

            if (root.ValueKind != JsonValueKind.Array) return new List<AvailabilitySlotDto>();
            return JsonSerializer.Deserialize<List<AvailabilitySlotDto>>(root.GetRawText(), JsonOptions) ?? new List<AvailabilitySlotDto>();
        }
        catch (JsonException)
        {
            return new List<AvailabilitySlotDto>();
        }
    }

    private static List<MatchupPair> BuildRoundRobin(List<string> teamIds)
    {
        var teams = new List<string>(teamIds);
        if (teams.Count % 2 == 1) teams.Add("BYE");

        var rounds = teams.Count - 1;
        var half = teams.Count / 2;
        var matchups = new List<MatchupPair>();

        for (var round = 0; round < rounds; round++)
        {
            for (var i = 0; i < half; i++)
            {
                var teamA = teams[i];
                var teamB = teams[teams.Count - 1 - i];
                if (teamA == "BYE" || teamB == "BYE") continue;

                var home = round % 2 == 0 ? teamA : teamB;
                var away = round % 2 == 0 ? teamB : teamA;
                matchups.Add(new MatchupPair(home, away));
            }

            var last = teams[^1];
            teams.RemoveAt(teams.Count - 1);
            teams.Insert(1, last);
        }

        return matchups;
    }

    private static ScheduleResult AssignMatchups(
        List<SlotInfo> slots,
        List<MatchupPair> matchups,
        List<string> teams,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        int externalOfferPerWeek,
        List<DayOfWeek> preferredDays)
    {
        if (preferredDays.Count > 0)
        {
            slots = slots
                .OrderBy(s => PreferredDayRank(s.gameDate, preferredDays))
                .ThenBy(s => s.gameDate)
                .ThenBy(s => s.startTime)
                .ThenBy(s => s.fieldKey)
                .ToList();
        }
        var teamSet = new HashSet<string>(teams, StringComparer.OrdinalIgnoreCase);
        var homeCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var awayCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var gamesByDate = teams.ToDictionary(t => t, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var gamesByWeek = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var matchupAssignments = new List<SlotAssignment>();
        var remainingMatchups = new List<MatchupPair>(matchups);
        var unassignedSlots = new List<ScheduleSlotDto>();
        var failures = new List<object>();
        var backtrackAttempts = 0;
        var backtrackSuccesses = 0;
        var backtrackFailures = 0;

        foreach (var slot in slots)
        {
            var fixedHome = teamSet.Contains(slot.offeringTeamId) ? slot.offeringTeamId : "";
            var pick = PickMatchup(slot, fixedHome, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway);
            if (pick is null)
            {
                backtrackAttempts++;
                if (TryBacktrackAssign(slot, teamSet, matchupAssignments, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, out var failureReason))
                {
                    backtrackSuccesses++;
                    continue;
                }

                backtrackFailures++;
                failures.Add(new { slotId = slot.slotId, gameDate = slot.gameDate, reason = failureReason });
                unassignedSlots.Add(new ScheduleSlotDto(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, "", "", false));
                continue;
            }

            ApplyMatchupAssignment(slot, pick, matchupAssignments, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek);
        }

        var assignments = matchupAssignments
            .Select(a => new ScheduleSlotDto(a.slot.slotId, a.slot.gameDate, a.slot.startTime, a.slot.endTime, a.slot.fieldKey, a.homeTeamId, a.awayTeamId, false))
            .ToList();

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
            unassignedMatchups = remainingMatchups.Count,
            backtrackAttempts,
            backtrackSuccesses,
            backtrackFailures
        };

        return new ScheduleResult(summary, assignments, unassignedSlots, unassignedMatchups, failures);
    }

    private static CandidateMatchup? PickMatchup(
        SlotInfo slot,
        string fixedHome,
        List<MatchupPair> matchups,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway)
    {
        var candidates = BuildMatchupCandidates(slot.gameDate, fixedHome, matchups, homeCounts, awayCounts, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway);
        return candidates.FirstOrDefault();
    }

    private static List<CandidateMatchup> BuildMatchupCandidates(
        string gameDate,
        string fixedHome,
        List<MatchupPair> matchups,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway)
    {
        var candidates = new List<CandidateMatchup>();
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

            candidates.Add(new CandidateMatchup(m, home, away, score));
        }

        return candidates
            .OrderBy(c => c.score)
            .ThenBy(c => c.home, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.away, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static void RemoveCounts(
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
            homeCounts[home] = Math.Max(0, homeCounts[home] - 1);
            gamesByDate[home].Remove(gameDate);
            RemoveWeekCount(gamesByWeek, home, gameDate);
        }
        if (!string.IsNullOrWhiteSpace(away))
        {
            awayCounts[away] = Math.Max(0, awayCounts[away] - 1);
            gamesByDate[away].Remove(gameDate);
            RemoveWeekCount(gamesByWeek, away, gameDate);
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

    private static void RemoveWeekCount(Dictionary<string, int> gamesByWeek, string teamId, string gameDate)
    {
        var weekKey = WeekKey(gameDate);
        if (string.IsNullOrWhiteSpace(weekKey)) return;
        var key = $"{teamId}|{weekKey}";
        if (!gamesByWeek.TryGetValue(key, out var v)) return;
        if (v <= 1) gamesByWeek.Remove(key);
        else gamesByWeek[key] = v - 1;
    }

    private static bool TryBacktrackAssign(
        SlotInfo currentSlot,
        HashSet<string> teamSet,
        List<SlotAssignment> matchupAssignments,
        List<MatchupPair> remainingMatchups,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        out string failureReason)
    {
        failureReason = "No eligible matchup for slot.";
        if (matchupAssignments.Count == 0) return false;

        const int MaxBacktrackDepth = 4;
        const int MaxBacktrackAttempts = 200;

        var depth = Math.Min(MaxBacktrackDepth, matchupAssignments.Count);
        var windowAssignments = matchupAssignments.Skip(matchupAssignments.Count - depth).ToList();
        var slotsToAssign = windowAssignments.Select(a => a.slot).ToList();
        slotsToAssign.Add(currentSlot);

        foreach (var assignment in windowAssignments)
        {
            UndoMatchupAssignment(assignment, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek);
        }
        matchupAssignments.RemoveRange(matchupAssignments.Count - depth, depth);

        var newAssignments = new List<SlotAssignment>();
        var attempts = 0;
        var success = BacktrackAssignSlots(slotsToAssign, 0, teamSet, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, newAssignments, ref attempts, MaxBacktrackAttempts, out failureReason);

        if (success)
        {
            matchupAssignments.AddRange(newAssignments);
            return true;
        }

        foreach (var assignment in newAssignments)
        {
            UndoMatchupAssignment(assignment, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek);
        }

        foreach (var assignment in windowAssignments)
        {
            ApplyMatchupAssignment(assignment.slot, new CandidateMatchup(assignment.matchup, assignment.homeTeamId, assignment.awayTeamId, 0), matchupAssignments, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek);
        }

        return false;
    }

    private static bool BacktrackAssignSlots(
        List<SlotInfo> slots,
        int index,
        HashSet<string> teamSet,
        List<MatchupPair> remainingMatchups,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        List<SlotAssignment> assignments,
        ref int attempts,
        int maxAttempts,
        out string failureReason)
    {
        failureReason = "No eligible matchup for slot.";
        if (index >= slots.Count) return true;
        if (attempts >= maxAttempts)
        {
            failureReason = "Backtracking attempt budget exceeded.";
            return false;
        }

        var slot = slots[index];
        var slotFixedHome = teamSet.Contains(slot.offeringTeamId) ? slot.offeringTeamId : "";

        var candidates = BuildMatchupCandidates(slot.gameDate, slotFixedHome, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway);
        foreach (var candidate in candidates)
        {
            if (attempts++ >= maxAttempts)
            {
                failureReason = "Backtracking attempt budget exceeded.";
                return false;
            }

            ApplyMatchupAssignment(slot, candidate, assignments, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek);
            if (BacktrackAssignSlots(slots, index + 1, teamSet, remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, assignments, ref attempts, maxAttempts, out failureReason))
                return true;

            UndoMatchupAssignment(assignments[^1], remainingMatchups, homeCounts, awayCounts, gamesByDate, gamesByWeek);
            assignments.RemoveAt(assignments.Count - 1);
        }

        failureReason = "No eligible matchup after backtracking.";
        return false;
    }

    private static void ApplyMatchupAssignment(
        SlotInfo slot,
        CandidateMatchup candidate,
        List<SlotAssignment> assignments,
        List<MatchupPair> remainingMatchups,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek)
    {
        remainingMatchups.Remove(candidate.source);
        ApplyCounts(candidate.home, candidate.away, slot.gameDate, homeCounts, awayCounts, gamesByDate, gamesByWeek);
        assignments.Add(new SlotAssignment(slot, candidate.source, candidate.home, candidate.away));
    }

    private static void UndoMatchupAssignment(
        SlotAssignment assignment,
        List<MatchupPair> remainingMatchups,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek)
    {
        remainingMatchups.Add(assignment.matchup);
        RemoveCounts(assignment.homeTeamId, assignment.awayTeamId, assignment.slot.gameDate, homeCounts, awayCounts, gamesByDate, gamesByWeek);
    }

    private async Task SaveScheduleRunAsync(
        string leagueId,
        string division,
        string runId,
        string createdBy,
        string dateFrom,
        string dateTo,
        ScheduleConstraints constraints,
        GameSwap.Functions.Scheduling.ScheduleResult result)
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
            ["MaxGamesPerWeek"] = constraints.MaxGamesPerWeek ?? 0,
            ["NoDoubleHeaders"] = constraints.NoDoubleHeaders,
            ["BalanceHomeAway"] = constraints.BalanceHomeAway,
            ["ExternalOfferPerWeek"] = constraints.ExternalOfferPerWeek,
            ["Summary"] = JsonSerializer.Serialize(result.Summary)
        };

        await table.AddEntityAsync(entity);
    }

    private async Task ApplyAssignmentsAsync(string leagueId, string division, string runId, List<ScheduleAssignment> assignments)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
        var pk = Constants.Pk.Slots(leagueId, division);

        foreach (var a in assignments)
        {
            TableEntity slot;
            try
            {
                slot = (await table.GetEntityAsync<TableEntity>(pk, a.SlotId)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                continue;
            }

            slot["OfferingTeamId"] = a.HomeTeamId;
            slot["HomeTeamId"] = a.HomeTeamId;
            slot["AwayTeamId"] = a.AwayTeamId ?? "";
            slot["IsExternalOffer"] = a.IsExternalOffer;
            slot["IsAvailability"] = false;
            slot["ScheduleRunId"] = runId;
            slot["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(slot, slot.ETag, TableUpdateMode.Merge);
        }
    }

    private static ScheduleSlotDto ToSlotDto(ScheduleAssignment assignment)
        => new(
            assignment.SlotId,
            assignment.GameDate,
            assignment.StartTime,
            assignment.EndTime,
            assignment.FieldKey,
            assignment.HomeTeamId,
            assignment.AwayTeamId,
            assignment.IsExternalOffer
        );

    private record AvailabilityExpansionRequest(string leagueId, string division, string? dateFrom, string? dateTo);
    private record AvailabilitySlotDto(string slotId, string gameDate, string startTime, string endTime, string fieldKey, string division, string? offeringTeamId);
    private record SlotInfo(string slotId, string gameDate, string startTime, string endTime, string fieldKey, string offeringTeamId);
    private record MatchupPair(string home, string away);
    private record CandidateMatchup(MatchupPair source, string home, string away, int score);
    private record SlotAssignment(SlotInfo slot, MatchupPair matchup, string homeTeamId, string awayTeamId);
    private record ScheduleResult(object summary, List<ScheduleSlotDto> assignments, List<ScheduleSlotDto> unassignedSlots, List<object> unassignedMatchups, List<object> failures);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static List<DayOfWeek> NormalizePreferredDays(List<string>? days)
    {
        var ordered = new List<DayOfWeek>();
        if (days is null) return ordered;
        foreach (var raw in days)
        {
            var key = (raw ?? "").Trim().ToLowerInvariant();
            if (key.StartsWith("sun")) ordered.Add(DayOfWeek.Sunday);
            else if (key.StartsWith("mon")) ordered.Add(DayOfWeek.Monday);
            else if (key.StartsWith("tue")) ordered.Add(DayOfWeek.Tuesday);
            else if (key.StartsWith("wed")) ordered.Add(DayOfWeek.Wednesday);
            else if (key.StartsWith("thu")) ordered.Add(DayOfWeek.Thursday);
            else if (key.StartsWith("fri")) ordered.Add(DayOfWeek.Friday);
            else if (key.StartsWith("sat")) ordered.Add(DayOfWeek.Saturday);
        }

        return ordered.Distinct().ToList();
    }

    private static int PreferredDayRank(string gameDate, List<DayOfWeek> preferredDays)
    {
        if (preferredDays.Count == 0) return int.MaxValue;
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return int.MaxValue;
        var index = preferredDays.IndexOf(dt.DayOfWeek);
        return index >= 0 ? index : int.MaxValue;
    }
}
