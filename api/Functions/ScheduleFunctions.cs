using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Scheduling;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

public class ScheduleFunctions
{
    private readonly ITeamRepository _teamRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly IScheduleRunRepository _scheduleRunRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger _log;

    public ScheduleFunctions(
        ITeamRepository teamRepo,
        ISlotRepository slotRepo,
        IScheduleRunRepository scheduleRunRepo,
        IMembershipRepository membershipRepo,
        ILoggerFactory lf)
    {
        _teamRepo = teamRepo;
        _slotRepo = slotRepo;
        _scheduleRunRepo = scheduleRunRepo;
        _membershipRepo = membershipRepo;
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

    [Function("ScheduleValidate")]
    public async Task<HttpResponseData> Validate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/validate")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            {
                return ApiResponses.Error(req, HttpStatusCode.Unauthorized,
                    ErrorCodes.UNAUTHENTICATED, "You must be signed in.");
            }

            // Authorization - league admin required
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var myRole = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(myRole, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can validate schedules");
                }
            }

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

            var assignments = await LoadScheduledAssignmentsAsync(leagueId, division, dateFrom, dateTo);
            if (assignments.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No scheduled games found in this range.");

            var summary = new ScheduleSummary(
                SlotsTotal: assignments.Count,
                SlotsAssigned: assignments.Count,
                MatchupsTotal: assignments.Count,
                MatchupsAssigned: assignments.Count,
                ExternalOffers: assignments.Count(a => a.IsExternalOffer),
                UnassignedSlots: 0,
                UnassignedMatchups: 0);

            var result = new GameSwap.Functions.Scheduling.ScheduleResult(
                summary,
                assignments,
                new List<ScheduleAssignment>(),
                new List<GameSwap.Functions.Scheduling.MatchupPair>());
            var validation = ScheduleValidation.Validate(result, scheduleConstraints);
            var issues = validation.Issues
                .Select(i => (object)new { ruleId = i.RuleId, severity = i.Severity, message = i.Message, details = i.Details })
                .ToList();

            return ApiResponses.Ok(req, new { summary, issues, totalIssues = validation.TotalIssues });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Schedule validate failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An unexpected error occurred",
                new { requestId, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    private async Task<HttpResponseData> RunSchedule(HttpRequestData req, bool apply)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            {
                return ApiResponses.Error(req, HttpStatusCode.Unauthorized,
                    ErrorCodes.UNAUTHENTICATED, "You must be signed in.");
            }

            // Authorization - league admin required
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var myRole = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(myRole, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can run schedules");
                }
            }

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
            {
                // Rerun support: if all availability rows were already converted into game slots by a prior run,
                // reuse those scheduler-managed game rows (while preserving practice/cancelled slots).
                slots = await LoadReusableScheduledGameSlotsAsync(leagueId, division, dateFrom, dateTo);
            }
            if (slots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No availability or reusable scheduled game slots found for this division.");

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
                await ApplyAssignmentsAsync(leagueId, division, runId, result.Assignments, result.UnassignedSlots);
                UsageTelemetry.Track(_log, "api_schedule_apply", leagueId, me.UserId, new
                {
                    division,
                    runId,
                    slotsAssigned = result.Assignments.Count,
                    issues = validation.TotalIssues
                });
                return ApiResponses.Ok(req, new { runId, result.Summary, assignments, unassignedSlots, unassignedMatchups, failures });
            }

            UsageTelemetry.Track(_log, "api_schedule_preview", leagueId, me.UserId, new
            {
                division,
                slotsAssigned = result.Assignments.Count,
                issues = validation.TotalIssues
            });
            return ApiResponses.Ok(req, new SchedulePreviewDto(result.Summary, assignments, unassignedSlots, unassignedMatchups, failures));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Schedule run failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An unexpected error occurred",
                new { requestId, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    private async Task<List<string>> LoadTeamsAsync(string leagueId, string division)
    {
        var teams = await _teamRepo.QueryTeamsByDivisionAsync(leagueId, division);
        var list = new List<string>();
        foreach (var e in teams)
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
            return await ExcludeBlockedExpandedSlotsAsync(leagueId, division, dateFrom, dateTo, expanded);

        return await LoadAvailabilitySlotsFromTableAsync(leagueId, division, dateFrom, dateTo);
    }

    private async Task<List<SlotInfo>> LoadAvailabilitySlotsFromTableAsync(string leagueId, string division, string dateFrom, string dateTo)
    {
        var filter = new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            Status = Constants.Status.SlotOpen,
            FromDate = dateFrom,
            ToDate = dateTo,
            PageSize = 1000
        };

        var result = await _slotRepo.QuerySlotsAsync(filter, null);
        var list = new List<SlotInfo>();

        foreach (var e in result.Items)
        {
            var isAvailability = SlotEntityUtil.IsAvailability(e);
            if (!isAvailability) continue;

            var homeTeamId = SlotEntityUtil.ReadString(e, "HomeTeamId");
            var awayTeamId = SlotEntityUtil.ReadString(e, "AwayTeamId");
            var isExternalOffer = SlotEntityUtil.ReadBool(e, "IsExternalOffer", false);
            if (!string.IsNullOrWhiteSpace(homeTeamId) || !string.IsNullOrWhiteSpace(awayTeamId) || isExternalOffer) continue;

            list.Add(new SlotInfo(
                slotId: e.RowKey,
                gameDate: SlotEntityUtil.ReadString(e, "GameDate"),
                startTime: SlotEntityUtil.ReadString(e, "StartTime"),
                endTime: SlotEntityUtil.ReadString(e, "EndTime"),
                fieldKey: SlotEntityUtil.ReadString(e, "FieldKey"),
                offeringTeamId: SlotEntityUtil.ReadString(e, "OfferingTeamId")
            ));
        }

        return list
            .Where(s => !string.IsNullOrWhiteSpace(s.gameDate))
            .OrderBy(s => s.gameDate)
            .ThenBy(s => s.startTime)
            .ThenBy(s => s.fieldKey)
            .ToList();
    }

    private async Task<List<SlotInfo>> ExcludeBlockedExpandedSlotsAsync(
        string leagueId,
        string division,
        string dateFrom,
        string dateTo,
        List<SlotInfo> expanded)
    {
        if (expanded.Count == 0) return expanded;

        var filter = new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            FromDate = dateFrom,
            ToDate = dateTo,
            PageSize = 1000
        };

        var result = await _slotRepo.QuerySlotsAsync(filter, null);
        var excludedSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in result.Items)
        {
            var slotId = (e.RowKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(slotId)) continue;

            var status = SlotEntityUtil.ReadString(e, "Status", Constants.Status.SlotOpen);
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
            {
                excludedSlotIds.Add(slotId);
                continue;
            }

            if (SlotEntityUtil.IsPractice(e))
            {
                excludedSlotIds.Add(slotId);
            }
        }

        if (excludedSlotIds.Count == 0) return expanded;

        return expanded
            .Where(s => !excludedSlotIds.Contains(s.slotId))
            .ToList();
    }

    private async Task<List<SlotInfo>> LoadReusableScheduledGameSlotsAsync(string leagueId, string division, string dateFrom, string dateTo)
    {
        var filter = new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            FromDate = dateFrom,
            ToDate = dateTo,
            PageSize = 1000
        };

        var result = await _slotRepo.QuerySlotsAsync(filter, null);
        var list = new List<SlotInfo>();

        foreach (var e in result.Items)
        {
            if (!SlotEntityUtil.IsReusableSchedulerGameSlot(e)) continue;

            list.Add(new SlotInfo(
                slotId: e.RowKey,
                gameDate: SlotEntityUtil.ReadString(e, "GameDate"),
                startTime: SlotEntityUtil.ReadString(e, "StartTime"),
                endTime: SlotEntityUtil.ReadString(e, "EndTime"),
                fieldKey: SlotEntityUtil.ReadString(e, "FieldKey"),
                offeringTeamId: SlotEntityUtil.ReadString(e, "OfferingTeamId")
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

        await _scheduleRunRepo.CreateScheduleRunAsync(entity);
    }

    private async Task ApplyAssignmentsAsync(
        string leagueId,
        string division,
        string runId,
        List<ScheduleAssignment> assignments,
        List<ScheduleAssignment> unassignedSlots)
    {
        foreach (var a in assignments)
        {
            var slot = await _slotRepo.GetSlotAsync(leagueId, division, a.SlotId);
            if (slot is null) continue;
            if (SlotEntityUtil.IsPractice(slot)) continue;

            SlotEntityUtil.ApplySchedulerAssignment(
                slot,
                runId,
                a.HomeTeamId,
                a.AwayTeamId,
                a.IsExternalOffer,
                confirmedBy: "Scheduler",
                nowUtc: DateTimeOffset.UtcNow);

            await _slotRepo.UpdateSlotAsync(slot, slot.ETag);
        }

        foreach (var s in unassignedSlots)
        {
            var slot = await _slotRepo.GetSlotAsync(leagueId, division, s.SlotId);
            if (slot is null) continue;
            if (SlotEntityUtil.IsPractice(slot)) continue;

            // When rerunning on existing scheduler-generated game rows, clear stale assignments
            // so unused rows become availability again.
            SlotEntityUtil.ResetSchedulerSlotToAvailability(slot, DateTimeOffset.UtcNow);

            await _slotRepo.UpdateSlotAsync(slot, slot.ETag);
        }
    }

    private async Task<List<ScheduleAssignment>> LoadScheduledAssignmentsAsync(string leagueId, string division, string dateFrom, string dateTo)
    {
        var filter = new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            FromDate = dateFrom,
            ToDate = dateTo,
            PageSize = 1000
        };

        var result = await _slotRepo.QuerySlotsAsync(filter, null);
        var list = new List<ScheduleAssignment>();

        foreach (var e in result.Items)
        {
            var status = SlotEntityUtil.ReadString(e, "Status", Constants.Status.SlotOpen);
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                continue;

            var isAvailability = SlotEntityUtil.ReadBool(e, "IsAvailability", false);
            if (isAvailability) continue;
            if (SlotEntityUtil.IsPractice(e)) continue;

            var home = SlotEntityUtil.ReadString(e, "HomeTeamId");
            var away = SlotEntityUtil.ReadString(e, "AwayTeamId");
            if (string.IsNullOrWhiteSpace(home) && string.IsNullOrWhiteSpace(away)) continue;

            list.Add(new ScheduleAssignment(
                SlotId: e.RowKey,
                GameDate: SlotEntityUtil.ReadString(e, "GameDate"),
                StartTime: SlotEntityUtil.ReadString(e, "StartTime"),
                EndTime: SlotEntityUtil.ReadString(e, "EndTime"),
                FieldKey: SlotEntityUtil.ReadString(e, "FieldKey"),
                HomeTeamId: home,
                AwayTeamId: away,
                IsExternalOffer: SlotEntityUtil.ReadBool(e, "IsExternalOffer", false)
            ));
        }

        return list
            .Where(a => !string.IsNullOrWhiteSpace(a.GameDate))
            .OrderBy(a => a.GameDate)
            .ThenBy(a => a.StartTime)
            .ThenBy(a => a.FieldKey)
            .ToList();
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

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

}
