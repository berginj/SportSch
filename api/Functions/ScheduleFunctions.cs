using System.Linq;
using System.Text.Json;
using System.Net;
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
        ScheduleSummary summary,
        List<ScheduleAssignment> assignments,
        List<ScheduleAssignment> unassignedSlots,
        List<MatchupPair> unassignedMatchups,
        ScheduleValidationResult validation
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

            var slots = await LoadOpenSlotsAsync(leagueId, division, dateFrom, dateTo);
            if (slots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No open slots found for this division.");

            var matchups = ScheduleEngine.BuildRoundRobin(teams);
            var scheduleSlots = slots.Select(slot => new ScheduleSlot(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, slot.offeringTeamId)).ToList();
            var result = ScheduleEngine.AssignMatchups(scheduleSlots, matchups, teams, scheduleConstraints);
            var validation = ScheduleValidation.Validate(result, scheduleConstraints);

            if (apply)
            {
                var runId = Guid.NewGuid().ToString("N");
                await SaveScheduleRunAsync(leagueId, division, runId, me.Email ?? me.UserId, dateFrom, dateTo, scheduleConstraints, result);
                await ApplyAssignmentsAsync(leagueId, division, runId, result.Assignments);
                return ApiResponses.Ok(req, new { runId, result.Summary, result.Assignments, result.UnassignedSlots, result.UnassignedMatchups, validation });
            }

            return ApiResponses.Ok(req, new SchedulePreviewDto(result.Summary, result.Assignments, result.UnassignedSlots, result.UnassignedMatchups, validation));
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

    private async Task SaveScheduleRunAsync(
        string leagueId,
        string division,
        string runId,
        string createdBy,
        string dateFrom,
        string dateTo,
        ScheduleConstraints constraints,
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

    private record SlotInfo(string slotId, string gameDate, string startTime, string endTime, string fieldKey, string offeringTeamId);
}
