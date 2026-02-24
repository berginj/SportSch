using System.Globalization;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Scheduling;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

public class ScheduleWizardFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public ScheduleWizardFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<ScheduleWizardFunctions>();
    }

    public record WizardRequest(
        string? division,
        string? seasonStart,
        string? seasonEnd,
        string? poolStart,
        string? poolEnd,
        string? bracketStart,
        string? bracketEnd,
        List<DateRangeOption>? blockedDateRanges,
        int? minGamesPerTeam,
        int? poolGamesPerTeam,
        List<string>? preferredWeeknights,
        bool? strictPreferredWeeknights,
        int? externalOfferPerWeek,
        int? maxGamesPerWeek,
        bool? noDoubleHeaders,
        bool? balanceHomeAway,
        List<SlotPlanItem>? slotPlan,
        GuestAnchorOption? guestAnchorPrimary,
        GuestAnchorOption? guestAnchorSecondary,
        string? constructionStrategy,
        int? seed
    );

    public record DateRangeOption(
        string? startDate,
        string? endDate,
        string? label
    );

    public record SlotPlanItem(
        string? slotId,
        string? slotType,
        int? priorityRank,
        string? startTime,
        string? endTime
    );

    public record GuestAnchorOption(
        string? dayOfWeek,
        string? startTime,
        string? endTime,
        string? fieldKey
    );

    public record PhaseSummary(
        string phase,
        int slotsTotal,
        int slotsAssigned,
        int matchupsTotal,
        int matchupsAssigned,
        int unassignedSlots,
        int unassignedMatchups
    );

    public record WizardSummary(
        PhaseSummary regularSeason,
        PhaseSummary poolPlay,
        PhaseSummary bracket,
        int totalSlots,
        int totalAssigned,
        int teamCount
    );

    public record WizardSlotDto(
        string phase,
        string slotId,
        string gameDate,
        string startTime,
        string endTime,
        string fieldKey,
        string homeTeamId,
        string awayTeamId,
        bool isExternalOffer
    );

    public record WizardPreviewDto(
        WizardSummary summary,
        List<WizardSlotDto> assignments,
        List<WizardSlotDto> unassignedSlots,
        List<object> unassignedMatchups,
        List<object> warnings,
        List<object> issues,
        int totalIssues,
        object? ruleHealth,
        List<object>? repairProposals,
        bool applyBlocked,
        int? seed,
        string? constructionStrategy,
        object? explanations
    );

    public record WizardPreviewRepairRequest(
        WizardRequest? wizard,
        WizardPreviewDto? preview,
        ScheduleRepairProposal? proposal
    );

    [Function("ScheduleWizardPreview")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/wizard/preview")] HttpRequestData req)
    {
        return await RunWizard(req, apply: false);
    }

    [Function("ScheduleWizardApply")]
    public async Task<HttpResponseData> Apply(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/wizard/apply")] HttpRequestData req)
    {
        return await RunWizard(req, apply: true);
    }

    [Function("ScheduleWizardFeasibility")]
    public async Task<HttpResponseData> Feasibility(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/wizard/feasibility")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<WizardRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division is required");
            ApiGuards.EnsureValidTableKeyPart("division", division);

            var seasonStart = RequireDate(body.seasonStart, "seasonStart");
            var seasonEnd = RequireDate(body.seasonEnd, "seasonEnd");
            if (seasonStart > seasonEnd)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "seasonStart must be before seasonEnd.");

            var poolStart = OptionalDate(body.poolStart);
            var poolEnd = OptionalDate(body.poolEnd);
            var bracketStart = OptionalDate(body.bracketStart);
            var bracketEnd = OptionalDate(body.bracketEnd);

            var minGamesPerTeam = Math.Max(0, body.minGamesPerTeam ?? 0);
            var poolGamesPerTeam = Math.Max(2, body.poolGamesPerTeam ?? 2);
            var externalOfferPerWeek = Math.Max(0, body.externalOfferPerWeek ?? 0);
            var maxGamesPerWeek = body.maxGamesPerWeek.GetValueOrDefault();
            if (maxGamesPerWeek < 0) maxGamesPerWeek = 0;
            var noDoubleHeaders = body.noDoubleHeaders ?? true;
            var blockedRanges = NormalizeBlockedDateRanges(body.blockedDateRanges);

            // Load teams
            var teams = await LoadTeamsAsync(leagueId, division);
            if (teams.Count < 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Need at least two teams to schedule.");

            // Load and filter slots
            var rawSlots = await LoadAvailabilitySlotsAsync(leagueId, division, seasonStart, bracketEnd ?? seasonEnd);
            if (rawSlots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No availability slots found for this division.");

            var slotPlanLookup = BuildSlotPlanLookup(body.slotPlan);
            var hasSlotPlan = body.slotPlan is not null && body.slotPlan.Count > 0;
            var allSlots = ApplySlotPlan(rawSlots, slotPlanLookup, hasSlotPlan);
            var filteredAllSlots = ApplyDateBlackouts(allSlots, blockedRanges);
            if (filteredAllSlots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No slots remain after applying blocked date ranges.");

            var gameCapableSlots = filteredAllSlots.Where(IsGameCapableSlotType).ToList();
            if (gameCapableSlots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No game-capable slots selected. Mark at least one slot as game or both.");

            // Calculate phase slot counts
            var regularRangeEnd = poolStart.HasValue ? poolStart.Value.AddDays(-1) : seasonEnd;
            var regularSlots = FilterSlots(gameCapableSlots, seasonStart, regularRangeEnd);
            var poolSlots = poolStart.HasValue && poolEnd.HasValue
                ? FilterSlots(filteredAllSlots, poolStart.Value, poolEnd.Value)
                : new List<SlotInfo>();
            var bracketSlots = bracketStart.HasValue && bracketEnd.HasValue
                ? FilterSlots(filteredAllSlots, bracketStart.Value, bracketEnd.Value)
                : new List<SlotInfo>();

            // Calculate active weeks in regular season from usable slots after blackouts.
            // This avoids over-counting blocked windows (e.g., Spring Break) in guest-slot reservation math.
            var regularWeeksCount = regularSlots
                .Select(s => WeekKey(s.gameDate))
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (regularWeeksCount <= 0)
            {
                regularWeeksCount = Math.Max(0, (regularRangeEnd.DayNumber - seasonStart.DayNumber) / 7 + 1);
            }

            // Run feasibility analysis
            var feasibilityResult = GameSwap.Scheduling.ScheduleFeasibility.Analyze(
                teamCount: teams.Count,
                availableRegularSlots: regularSlots.Count,
                availablePoolSlots: poolSlots.Count,
                availableBracketSlots: bracketSlots.Count,
                minGamesPerTeam: minGamesPerTeam,
                poolGamesPerTeam: poolGamesPerTeam,
                maxGamesPerWeek: maxGamesPerWeek,
                noDoubleHeaders: noDoubleHeaders,
                regularWeeksCount: regularWeeksCount,
                guestGamesPerWeek: externalOfferPerWeek
            );

            return ApiResponses.Ok(req, feasibilityResult);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error in ScheduleWizardFeasibility");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An error occurred while analyzing feasibility.",
                new { requestId, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    [Function("ScheduleWizardApplyPreviewRepair")]
    public async Task<HttpResponseData> ApplyPreviewRepair(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/wizard/repair/apply-preview")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<WizardPreviewRepairRequest>(req);
            if (body is null || body.wizard is null || body.preview is null || body.proposal is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "wizard, preview, and proposal are required.");

            var division = (body.wizard.division ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "division is required");
            ApiGuards.EnsureValidTableKeyPart("division", division);

            if (body.proposal.RequiresUserAction)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Manual-action repair proposals cannot be auto-applied.");

            var teams = await LoadTeamsAsync(leagueId, division);
            var blockedRanges = NormalizeBlockedDateRanges(body.wizard.blockedDateRanges);
            var validationConfig = BuildValidationConfig(
                maxGamesPerWeek: (body.wizard.maxGamesPerWeek ?? 0) <= 0 ? null : body.wizard.maxGamesPerWeek,
                noDoubleHeaders: body.wizard.noDoubleHeaders ?? true,
                balanceHomeAway: body.wizard.balanceHomeAway ?? true,
                blockedRanges: blockedRanges);

            var updatedPreview = ApplyRepairProposalToPreviewSnapshot(body.preview, body.proposal, teams, validationConfig);
            return ApiResponses.Ok(req, updatedPreview);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Schedule wizard preview repair failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An unexpected error occurred",
                new { requestId, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    private async Task<HttpResponseData> RunWizard(HttpRequestData req, bool apply)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<WizardRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division is required");
            ApiGuards.EnsureValidTableKeyPart("division", division);

            var seasonStart = RequireDate(body.seasonStart, "seasonStart");
            var seasonEnd = RequireDate(body.seasonEnd, "seasonEnd");
            if (seasonStart > seasonEnd)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "seasonStart must be before seasonEnd.");

            var poolStart = OptionalDate(body.poolStart);
            var poolEnd = OptionalDate(body.poolEnd);
            var bracketStart = OptionalDate(body.bracketStart);
            var bracketEnd = OptionalDate(body.bracketEnd);

            if ((poolStart.HasValue && !poolEnd.HasValue) || (!poolStart.HasValue && poolEnd.HasValue))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "poolStart and poolEnd must both be set.");
            if ((bracketStart.HasValue && !bracketEnd.HasValue) || (!bracketStart.HasValue && bracketEnd.HasValue))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "bracketStart and bracketEnd must both be set.");

            if (poolStart.HasValue && poolEnd.HasValue && poolStart.Value > poolEnd.Value)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "poolStart must be before poolEnd.");
            if (bracketStart.HasValue && bracketEnd.HasValue && bracketStart.Value > bracketEnd.Value)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "bracketStart must be before bracketEnd.");

            if (poolStart.HasValue && poolEnd.HasValue && (poolStart.Value < seasonStart || poolEnd.Value > seasonEnd))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Pool play must be within the season range.");
            if (bracketStart.HasValue && bracketEnd.HasValue && bracketStart.Value < seasonStart)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Bracket must start on or after the season start.");

            var minGamesPerTeam = Math.Max(0, body.minGamesPerTeam ?? 0);
            var poolGamesPerTeam = Math.Max(2, body.poolGamesPerTeam ?? 2);
            var externalOfferPerWeek = Math.Max(0, body.externalOfferPerWeek ?? 0);
            var maxGamesPerWeek = (body.maxGamesPerWeek ?? 0) <= 0 ? (int?)null : body.maxGamesPerWeek;
            var noDoubleHeaders = body.noDoubleHeaders ?? true;
            var balanceHomeAway = body.balanceHomeAway ?? true;
            var preferredDays = NormalizePreferredDays(body.preferredWeeknights);
            var strictPreferredWeeknights = body.strictPreferredWeeknights ?? false;
            var blockedRanges = NormalizeBlockedDateRanges(body.blockedDateRanges);
            var normalizedConstructionStrategy = NormalizeConstructionStrategy(body.constructionStrategy);
            var useBackwardRegularSeason = string.Equals(normalizedConstructionStrategy, "backward_greedy_v1", StringComparison.OrdinalIgnoreCase);
            var requestedSeed = body.seed.HasValue ? Math.Abs(body.seed.Value) : (int?)null;

            var teams = await LoadTeamsAsync(leagueId, division);
            if (teams.Count < 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Need at least two teams to schedule.");

            var rawSlots = await LoadAvailabilitySlotsAsync(leagueId, division, seasonStart, bracketEnd ?? seasonEnd);
            if (rawSlots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No availability slots found for this division.");
            var slotPlanLookup = BuildSlotPlanLookup(body.slotPlan);
            var hasSlotPlan = body.slotPlan is not null && body.slotPlan.Count > 0;
            var allSlots = ApplySlotPlan(rawSlots, slotPlanLookup, hasSlotPlan);
            var totalGameCapableSlots = allSlots.Count(IsGameCapableSlotType);
            var filteredAllSlots = ApplyDateBlackouts(allSlots, blockedRanges);
            var blockedOutSlots = Math.Max(0, allSlots.Count - filteredAllSlots.Count);
            if (filteredAllSlots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No slots remain after applying blocked date ranges.");
            var gameCapableSlots = filteredAllSlots.Where(IsGameCapableSlotType).ToList();
            if (gameCapableSlots.Count == 0)
            {
                if (totalGameCapableSlots > 0)
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No game-capable slots remain after applying blocked date ranges.");
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No game-capable slots selected. Mark at least one slot as game or both.");
            }
            var guestAnchors = NormalizeGuestAnchors(body.guestAnchorPrimary, body.guestAnchorSecondary);

            var regularRangeEnd = poolStart.HasValue ? poolStart.Value.AddDays(-1) : seasonEnd;
            var regularSlots = FilterSlots(gameCapableSlots, seasonStart, regularRangeEnd);
            var poolSlots = poolStart.HasValue && poolEnd.HasValue
                ? FilterSlots(filteredAllSlots, poolStart.Value, poolEnd.Value)
                : new List<SlotInfo>();
            var bracketSlots = bracketStart.HasValue && bracketEnd.HasValue
                ? FilterSlots(filteredAllSlots, bracketStart.Value, bracketEnd.Value)
                : new List<SlotInfo>();

            var regularMatchups = BuildRepeatedMatchups(teams, minGamesPerTeam);
            var poolMatchups = BuildTargetMatchups(teams, poolGamesPerTeam);
            var bracketMatchups = BuildBracketMatchups();

            var regularAssignments = AssignPhaseSlots("Regular Season", regularSlots, regularMatchups, teams, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, externalOfferPerWeek, preferredDays, strictPreferredWeeknights, guestAnchors, scheduleBackward: useBackwardRegularSeason);
            var poolAssignments = AssignPhaseSlots("Pool Play", poolSlots, poolMatchups, teams, null, noDoubleHeaders, balanceHomeAway, 0, preferredDays: new List<DayOfWeek>(), strictPreferredWeeknights: false, guestAnchors: null, scheduleBackward: false);
            var bracketAssignments = AssignBracketSlots(bracketSlots, bracketMatchups);
            var totalPhaseSlots = regularSlots
                .Select(s => s.slotId)
                .Concat(poolSlots.Select(s => s.slotId))
                .Concat(bracketSlots.Select(s => s.slotId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var summary = new WizardSummary(
                regularSeason: BuildPhaseSummary("Regular Season", regularSlots.Count, regularAssignments.Assignments.Count, regularMatchups.Count, regularAssignments.UnassignedMatchups.Count),
                poolPlay: BuildPhaseSummary("Pool Play", poolSlots.Count, poolAssignments.Assignments.Count, poolMatchups.Count, poolAssignments.UnassignedMatchups.Count),
                bracket: BuildPhaseSummary("Bracket", bracketSlots.Count, bracketAssignments.Assignments.Count, bracketMatchups.Count, bracketAssignments.UnassignedMatchups.Count),
                totalSlots: totalPhaseSlots,
                totalAssigned: regularAssignments.Assignments.Count + poolAssignments.Assignments.Count + bracketAssignments.Assignments.Count,
                teamCount: teams.Count
            );

            var assignments = new List<WizardSlotDto>();
            assignments.AddRange(regularAssignments.Assignments.Select(a => ToWizardSlot("Regular Season", a)));
            assignments.AddRange(poolAssignments.Assignments.Select(a => ToWizardSlot("Pool Play", a)));
            assignments.AddRange(bracketAssignments.Assignments.Select(a => ToWizardSlot("Bracket", a)));

            var unassignedSlots = new List<WizardSlotDto>();
            unassignedSlots.AddRange(regularAssignments.UnassignedSlots.Select(a => ToWizardSlot("Regular Season", a)));
            unassignedSlots.AddRange(poolAssignments.UnassignedSlots.Select(a => ToWizardSlot("Pool Play", a)));
            unassignedSlots.AddRange(bracketAssignments.UnassignedSlots.Select(a => ToWizardSlot("Bracket", a)));

            var unassignedMatchups = new List<object>();
            unassignedMatchups.AddRange(regularAssignments.UnassignedMatchups.Select(m => (object)new { phase = "Regular Season", homeTeamId = m.HomeTeamId, awayTeamId = m.AwayTeamId }));
            unassignedMatchups.AddRange(poolAssignments.UnassignedMatchups.Select(m => (object)new { phase = "Pool Play", homeTeamId = m.HomeTeamId, awayTeamId = m.AwayTeamId }));
            unassignedMatchups.AddRange(bracketAssignments.UnassignedMatchups.Select(m => (object)new { phase = "Bracket", homeTeamId = m.HomeTeamId, awayTeamId = m.AwayTeamId }));

            var warnings = new List<object>();
            if (regularSlots.Count == 0) warnings.Add(new { code = "NO_REGULAR_SLOTS", message = "No regular season slots available." });
            if (poolStart.HasValue && poolSlots.Count == 0) warnings.Add(new { code = "NO_POOL_SLOTS", message = "No pool play slots available." });
            if (bracketStart.HasValue && bracketSlots.Count == 0) warnings.Add(new { code = "NO_BRACKET_SLOTS", message = "No bracket slots available." });
            if (blockedOutSlots > 0) warnings.Add(new { code = "SLOTS_BLOCKED_BY_DATES", message = $"{blockedOutSlots} slot(s) were excluded by blocked date ranges." });
            if (externalOfferPerWeek > 0)
            {
                var externalAssignments = regularAssignments.Assignments.Where(a => a.IsExternalOffer).ToList();
                if (externalAssignments.Count == 0)
                {
                    warnings.Add(new { code = "NO_GUEST_GAMES", message = "No guest game offers could be created with the current slots and constraints." });
                }
                else if (externalAssignments.Count < teams.Count)
                {
                    warnings.Add(new { code = "GUEST_GAMES_INCOMPLETE", message = "Not every team has a guest game offer yet." });
                }
            }

            var constraints = new ScheduleConstraints(maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, 0);
            var validationSummary = new ScheduleSummary(
                SlotsTotal: regularSlots.Count,
                SlotsAssigned: regularAssignments.Assignments.Count,
                MatchupsTotal: regularMatchups.Count,
                MatchupsAssigned: regularMatchups.Count - regularAssignments.UnassignedMatchups.Count,
                ExternalOffers: 0,
                UnassignedSlots: regularAssignments.UnassignedSlots.Count,
                UnassignedMatchups: regularAssignments.UnassignedMatchups.Count);
            var validationResult = new ScheduleResult(validationSummary, regularAssignments.Assignments, regularAssignments.UnassignedSlots, regularAssignments.UnassignedMatchups);
            var validationConfig = new ScheduleValidationV2Config(
                MaxGamesPerWeek: maxGamesPerWeek,
                NoDoubleHeaders: noDoubleHeaders,
                BalanceHomeAway: balanceHomeAway,
                BlackoutWindows: blockedRanges
                    .Select((b, idx) => new ScheduleBlackoutWindow(
                        RuleId: $"blackout-{idx + 1}",
                        StartDate: b.StartDate,
                        EndDate: b.EndDate,
                        Label: string.IsNullOrWhiteSpace(b.Label) ? $"Blocked range {idx + 1}" : b.Label))
                    .ToList(),
                TreatUnassignedRequiredMatchupsAsHard: true);
            var strictValidation = ScheduleValidationV2.Validate(validationResult, validationConfig, teams);
            var issues = strictValidation.RuleHealth.Groups
                .Select(g => (object)new
                {
                    phase = "Regular Season",
                    ruleId = g.RuleId,
                    severity = string.Equals(g.Severity, "hard", StringComparison.OrdinalIgnoreCase) ? "error" : "warning",
                    message = g.Summary,
                    details = new Dictionary<string, object?>
                    {
                        ["count"] = g.Count,
                        ["severity"] = g.Severity,
                        ["smallestAffectedSet"] = g.SmallestAffectedSet
                    }
                })
                .ToList();
            if (poolAssignments.UnassignedMatchups.Count > 0)
            {
                issues.Add(new
                {
                    phase = "Pool Play",
                    ruleId = "unassigned-matchups",
                    severity = "warning",
                    message = $"{poolAssignments.UnassignedMatchups.Count} pool play matchup(s) were not assigned.",
                    details = new Dictionary<string, object?> { ["count"] = poolAssignments.UnassignedMatchups.Count, ["phase"] = "Pool Play" }
                });
            }
            if (bracketAssignments.UnassignedMatchups.Count > 0)
            {
                issues.Add(new
                {
                    phase = "Bracket",
                    ruleId = "unassigned-matchups",
                    severity = "warning",
                    message = $"{bracketAssignments.UnassignedMatchups.Count} bracket matchup(s) were not assigned.",
                    details = new Dictionary<string, object?> { ["count"] = bracketAssignments.UnassignedMatchups.Count, ["phase"] = "Bracket" }
                });
            }
            var totalIssues = issues.Count;
            var applyBlocked = strictValidation.RuleHealth.ApplyBlocked;
            var repairProposals = ScheduleRepairEngine.Propose(validationResult, strictValidation.RuleHealth, validationConfig, teams, maxProposals: 8)
                .Cast<object>()
                .ToList();
            var seed = requestedSeed ?? StableWizardSeed(division, seasonStart, seasonEnd);
            var constructionStrategy = $"{normalizedConstructionStrategy}+strict_validation_v2";
            var explanations = BuildWizardPlacementExplanations(regularAssignments, normalizedConstructionStrategy, seed);

            if (apply)
            {
                if (applyBlocked)
                {
                    var requestId = req.FunctionContext.InvocationId.ToString();
                    return ApiResponses.Error(
                        req,
                        HttpStatusCode.Conflict,
                        ErrorCodes.SCHEDULE_BLOCKED,
                        "Schedule has hard rule violations. Review Rule Health and repair suggestions before applying.",
                        new
                        {
                            requestId,
                            ruleHealth = strictValidation.RuleHealth,
                            hardViolations = strictValidation.RuleHealth.HardViolationCount,
                            totalIssues
                        });
                }
                var runId = Guid.NewGuid().ToString("N");
                await ApplyAssignmentsAsync(leagueId, division, runId, assignments);
                await SaveWizardRunAsync(leagueId, division, runId, me.Email ?? me.UserId, summary, body);
            }

            UsageTelemetry.Track(_log, apply ? "api_schedule_wizard_apply" : "api_schedule_wizard_preview", leagueId, me.UserId, new
            {
                division,
                slotsTotal = summary.totalSlots,
                assignedTotal = summary.totalAssigned,
                issues = totalIssues,
                applyBlocked,
                constructionStrategy,
                seed
            });

            return ApiResponses.Ok(req, new WizardPreviewDto(summary, assignments, unassignedSlots, unassignedMatchups, warnings, issues, totalIssues, strictValidation.RuleHealth, repairProposals, applyBlocked, seed, constructionStrategy, explanations));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Schedule wizard failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An unexpected error occurred",
                new { requestId, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    private static DateOnly RequireDate(string? raw, string field)
    {
        if (string.IsNullOrWhiteSpace(raw) || !DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var date))
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, $"{field} must be YYYY-MM-DD.");
        return date;
    }

    private static DateOnly? OptionalDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var date) ? date : null;
    }

    private static PhaseSummary BuildPhaseSummary(string phase, int slotsTotal, int assigned, int matchupsTotal, int unassignedMatchups)
    {
        return new PhaseSummary(
            phase,
            slotsTotal,
            assigned,
            matchupsTotal,
            matchupsTotal - unassignedMatchups,
            Math.Max(0, slotsTotal - assigned),
            unassignedMatchups
        );
    }

    private static int StableWizardSeed(string division, DateOnly seasonStart, DateOnly seasonEnd)
    {
        var text = $"{division}|{seasonStart:yyyyMMdd}|{seasonEnd:yyyyMMdd}";
        unchecked
        {
            var hash = 17;
            foreach (var ch in text)
            {
                hash = (hash * 31) + ch;
            }
            return Math.Abs(hash);
        }
    }

    private static string NormalizeConstructionStrategy(string? raw)
    {
        var value = (raw ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "legacy" or "legacy_greedy" or "legacy_greedy_v1" => "legacy_greedy_v1",
            "backward" or "backward_greedy" or "backward_greedy_v1" => "backward_greedy_v1",
            _ => "backward_greedy_v1" // Default to backward construction for regular-season scheduling.
        };
    }

    private static ScheduleValidationV2Config BuildValidationConfig(
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        List<BlockedDateRange> blockedRanges)
    {
        return new ScheduleValidationV2Config(
            MaxGamesPerWeek: maxGamesPerWeek,
            NoDoubleHeaders: noDoubleHeaders,
            BalanceHomeAway: balanceHomeAway,
            BlackoutWindows: blockedRanges
                .Select((b, idx) => new ScheduleBlackoutWindow(
                    RuleId: $"blackout-{idx + 1}",
                    StartDate: b.StartDate,
                    EndDate: b.EndDate,
                    Label: string.IsNullOrWhiteSpace(b.Label) ? $"Blocked range {idx + 1}" : b.Label))
                .ToList(),
            TreatUnassignedRequiredMatchupsAsHard: true);
    }

    private static WizardPreviewDto ApplyRepairProposalToPreviewSnapshot(
        WizardPreviewDto preview,
        ScheduleRepairProposal proposal,
        IReadOnlyList<string> teams,
        ScheduleValidationV2Config validationConfig)
    {
        var assignments = (preview.assignments ?? new List<WizardSlotDto>()).Select(a => a with { }).ToList();
        var unassignedSlots = (preview.unassignedSlots ?? new List<WizardSlotDto>()).Select(a => a with { }).ToList();
        var unassignedMatchups = preview.unassignedMatchups ?? new List<object>();
        var warnings = preview.warnings ?? new List<object>();

        if (!TryExtractMoveChanges(proposal, out var moves) || moves.Count == 0)
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, "Only move/swap repair proposals are supported right now.");

        var regularAssignmentsBySlot = assignments
            .Select((a, idx) => new { a, idx })
            .Where(x => string.Equals(x.a.phase, "Regular Season", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.a.slotId))
            .ToDictionary(x => x.a.slotId, x => x.idx, StringComparer.OrdinalIgnoreCase);
        var sourceSlotIds = new HashSet<string>(moves.Select(m => m.FromSlotId), StringComparer.OrdinalIgnoreCase);
        var targetSlotIds = new HashSet<string>(moves.Select(m => m.ToSlotId), StringComparer.OrdinalIgnoreCase);

        foreach (var move in moves)
        {
            if (!regularAssignmentsBySlot.TryGetValue(move.FromSlotId, out var idx))
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, "A source game could not be found in the current preview.");

            var currentAssignment = assignments[idx];
            if (!string.Equals(currentAssignment.homeTeamId, move.BeforeHomeTeamId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentAssignment.awayTeamId, move.BeforeAwayTeamId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, "The preview changed and this repair proposal is stale. Run preview again.");
            }
        }

        var requiredOpenTargetIds = targetSlotIds
            .Where(slotId => !sourceSlotIds.Contains(slotId))
            .ToList();
        foreach (var slotId in requiredOpenTargetIds)
        {
            var exists = unassignedSlots.Any(s =>
                string.Equals(s.phase, "Regular Season", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.slotId, slotId, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, "One or more target open slots are no longer available in the current preview.");
        }

        // Apply all moves atomically to assignments first (supports swap proposals as two reciprocal move diffs).
        foreach (var move in moves)
        {
            var idx = regularAssignmentsBySlot[move.FromSlotId];
            var currentAssignment = assignments[idx];
            assignments[idx] = currentAssignment with
            {
                slotId = move.ToSlotId,
                gameDate = move.ToGameDate,
                startTime = move.ToStartTime,
                endTime = move.ToEndTime,
                fieldKey = move.ToFieldKey
            };
        }

        // Consume target unused slots that came from open capacity (not another moved game's slot).
        unassignedSlots = unassignedSlots
            .Where(s => !(string.Equals(s.phase, "Regular Season", StringComparison.OrdinalIgnoreCase)
                && requiredOpenTargetIds.Contains(s.slotId ?? "", StringComparer.OrdinalIgnoreCase)))
            .ToList();

        // Free any source slots that are not reused by another move target.
        foreach (var move in moves.Where(m => !targetSlotIds.Contains(m.FromSlotId)))
        {
            unassignedSlots.Add(new WizardSlotDto(
                phase: "Regular Season",
                slotId: move.FromSlotId,
                gameDate: move.FromGameDate,
                startTime: move.FromStartTime,
                endTime: move.FromEndTime,
                fieldKey: move.FromFieldKey,
                homeTeamId: "",
                awayTeamId: "",
                isExternalOffer: false));
        }

        var regularAssignments = assignments
            .Where(a => string.Equals(a.phase, "Regular Season", StringComparison.OrdinalIgnoreCase))
            .Select(ToScheduleAssignment)
            .ToList();
        var regularUnassignedSlots = unassignedSlots
            .Where(a => string.Equals(a.phase, "Regular Season", StringComparison.OrdinalIgnoreCase))
            .Select(ToScheduleAssignment)
            .ToList();
        var regularUnassignedMatchups = ParsePreviewUnassignedMatchups(unassignedMatchups, "Regular Season");

        var regularSummary = preview.summary.regularSeason;
        var validationSummary = new ScheduleSummary(
            SlotsTotal: regularSummary.slotsTotal,
            SlotsAssigned: regularAssignments.Count,
            MatchupsTotal: regularSummary.matchupsTotal,
            MatchupsAssigned: regularSummary.matchupsTotal - regularUnassignedMatchups.Count,
            ExternalOffers: regularAssignments.Count(a => a.IsExternalOffer),
            UnassignedSlots: regularUnassignedSlots.Count,
            UnassignedMatchups: regularUnassignedMatchups.Count);
        var regularResult = new ScheduleResult(validationSummary, regularAssignments, regularUnassignedSlots, regularUnassignedMatchups);

        var strictValidation = ScheduleValidationV2.Validate(regularResult, validationConfig, teams);
        var regularIssues = BuildRegularSeasonIssuesFromRuleHealth(strictValidation.RuleHealth);
        var otherIssues = (preview.issues ?? new List<object>())
            .Where(issue => !string.Equals(GetIssuePhaseFromObject(issue), "Regular Season", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var mergedIssues = new List<object>(otherIssues.Count + regularIssues.Count);
        mergedIssues.AddRange(otherIssues);
        mergedIssues.AddRange(regularIssues);

        var repairProposals = ScheduleRepairEngine.Propose(regularResult, strictValidation.RuleHealth, validationConfig, teams, maxProposals: 8)
            .Cast<object>()
            .ToList();
        var updatedExplanations = BuildPreviewRepairExplanations(preview.explanations, assignments, moves);

        return new WizardPreviewDto(
            summary: preview.summary,
            assignments: assignments,
            unassignedSlots: unassignedSlots
                .OrderBy(s => s.phase)
                .ThenBy(s => s.gameDate)
                .ThenBy(s => s.startTime)
                .ThenBy(s => s.fieldKey)
                .ToList(),
            unassignedMatchups: unassignedMatchups,
            warnings: warnings,
            issues: mergedIssues,
            totalIssues: mergedIssues.Count,
            ruleHealth: strictValidation.RuleHealth,
            repairProposals: repairProposals,
            applyBlocked: strictValidation.RuleHealth.ApplyBlocked,
            seed: preview.seed,
            constructionStrategy: preview.constructionStrategy,
            explanations: updatedExplanations
        );
    }

    private static List<object> BuildRegularSeasonIssuesFromRuleHealth(ScheduleRuleHealthReport ruleHealth)
    {
        return (ruleHealth.Groups ?? Array.Empty<ScheduleRuleGroupReport>())
            .Select(g => (object)new
            {
                phase = "Regular Season",
                ruleId = g.RuleId,
                severity = string.Equals(g.Severity, "hard", StringComparison.OrdinalIgnoreCase) ? "error" : "warning",
                message = g.Summary,
                details = new Dictionary<string, object?>
                {
                    ["count"] = g.Count,
                    ["severity"] = g.Severity,
                    ["smallestAffectedSet"] = g.SmallestAffectedSet
                }
            })
            .ToList();
    }

    private static Dictionary<string, object> BuildWizardPlacementExplanations(
        PhaseAssignments regularAssignments,
        string normalizedConstructionStrategy,
        int seed)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var traces = regularAssignments.PlacementTraces ?? new List<SchedulePlacementTrace>();
        var slotOrderDirection = string.Equals(normalizedConstructionStrategy, "backward_greedy_v1", StringComparison.OrdinalIgnoreCase)
            ? "backward"
            : "forward";

        foreach (var trace in traces.Where(t => string.Equals(t.Outcome, "assigned", StringComparison.OrdinalIgnoreCase)))
        {
            var home = trace.SelectedHomeTeamId ?? "";
            var away = trace.SelectedAwayTeamId ?? "";
            var key = BuildWizardAssignmentExplainKey(
                phase: "Regular Season",
                slotId: trace.SlotId,
                gameDate: trace.GameDate,
                startTime: trace.StartTime,
                homeTeamId: home,
                awayTeamId: away);

            result[key] = new
            {
                source = "schedule_engine_trace_v1",
                phase = "Regular Season",
                outcome = trace.Outcome,
                placementRank = trace.SlotOrderIndex + 1,
                slotOrderIndex = trace.SlotOrderIndex,
                slotOrderDirection,
                seed,
                fixedHomeTeamId = trace.FixedHomeTeamId,
                candidateCount = trace.CandidateCount,
                feasibleCandidateCount = trace.FeasibleCandidateCount,
                selectedScore = trace.SelectedScoreBreakdown?.TotalScore,
                scoreBreakdown = ToExplainScoreBreakdown(trace.SelectedScoreBreakdown),
                topFeasibleAlternatives = trace.TopFeasibleAlternatives
                    .Select(c => (object)new
                    {
                        homeTeamId = c.HomeTeamId,
                        awayTeamId = c.AwayTeamId,
                        score = c.ScoreBreakdown?.TotalScore,
                        scoreBreakdown = ToExplainScoreBreakdown(c.ScoreBreakdown)
                    })
                    .ToList(),
                topRejectedAlternatives = trace.TopRejectedAlternatives
                    .Select(c => (object)new
                    {
                        homeTeamId = c.HomeTeamId,
                        awayTeamId = c.AwayTeamId,
                        rejectReason = c.RejectReason
                    })
                    .ToList()
            };
        }

        foreach (var external in regularAssignments.Assignments.Where(a => a.IsExternalOffer))
        {
            var key = BuildWizardAssignmentExplainKey("Regular Season", external);
            if (result.ContainsKey(key)) continue;
            result[key] = new
            {
                source = "external_offer_builder_v1",
                phase = "Regular Season",
                outcome = "external-offer",
                slotOrderDirection,
                seed,
                note = "Guest/external offers are generated after matchup assignment from remaining open slots."
            };
        }

        return result;
    }

    private static object? ToExplainScoreBreakdown(ScheduleScoreBreakdown? score)
    {
        if (score is null) return null;
        return new
        {
            totalScore = score.TotalScore,
            teamVolumePenalty = score.TeamVolumePenalty,
            teamImbalancePenalty = score.TeamImbalancePenalty,
            teamLoadSpreadPenalty = score.TeamLoadSpreadPenalty,
            homeAwayPenalty = score.HomeAwayPenalty
        };
    }

    private static string BuildWizardAssignmentExplainKey(string phase, ScheduleAssignment assignment)
        => BuildWizardAssignmentExplainKey(phase, assignment.SlotId, assignment.GameDate, assignment.StartTime, assignment.HomeTeamId, assignment.AwayTeamId);

    private static string BuildWizardAssignmentExplainKey(
        string phase,
        string slotId,
        string gameDate,
        string startTime,
        string homeTeamId,
        string awayTeamId)
    {
        return string.Join("|", new[]
        {
            (phase ?? "").Trim(),
            (slotId ?? "").Trim(),
            (gameDate ?? "").Trim(),
            (startTime ?? "").Trim(),
            (homeTeamId ?? "").Trim(),
            (awayTeamId ?? "").Trim()
        });
    }

    private static string GetIssuePhaseFromObject(object? issue)
    {
        if (issue is null) return "";
        if (issue is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("phase", out var phaseEl))
                    return phaseEl.GetString() ?? "";
                if (el.TryGetProperty("details", out var detailsEl) && detailsEl.ValueKind == JsonValueKind.Object &&
                    detailsEl.TryGetProperty("phase", out var detailsPhase))
                    return detailsPhase.GetString() ?? "";
            }
            return "";
        }
        try
        {
            var serialized = JsonSerializer.Serialize(issue);
            using var doc = JsonDocument.Parse(serialized);
            return GetIssuePhaseFromObject(doc.RootElement);
        }
        catch
        {
            return "";
        }
    }

    private static Dictionary<string, object> BuildPreviewRepairExplanations(
        object? existingExplanations,
        List<WizardSlotDto> assignments,
        List<PreviewMoveChange> moves)
    {
        var map = ReadObjectMap(existingExplanations);
        var movedByAfterKey = new Dictionary<string, PreviewMoveChange>(StringComparer.OrdinalIgnoreCase);
        var movedBeforeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var move in moves ?? new List<PreviewMoveChange>())
        {
            var beforeKey = BuildWizardAssignmentExplainKey(
                "Regular Season",
                move.FromSlotId,
                move.FromGameDate,
                move.FromStartTime,
                move.BeforeHomeTeamId,
                move.BeforeAwayTeamId);
            var afterKey = BuildWizardAssignmentExplainKey(
                "Regular Season",
                move.ToSlotId,
                move.ToGameDate,
                move.ToStartTime,
                move.BeforeHomeTeamId,
                move.BeforeAwayTeamId);

            movedBeforeKeys.Add(beforeKey);
            movedByAfterKey[afterKey] = move;

            if (map.TryGetValue(beforeKey, out var previousTrace))
            {
                map.Remove(beforeKey);
                map[afterKey] = BuildPreviewRepairMovedTrace(previousTrace, move);
            }
            else if (!map.ContainsKey(afterKey))
            {
                map[afterKey] = BuildPreviewRepairMovedTrace(previousTrace: null, move);
            }
        }

        foreach (var assignment in assignments.Where(a => string.Equals(a.phase, "Regular Season", StringComparison.OrdinalIgnoreCase)))
        {
            var key = BuildWizardAssignmentExplainKey(
                phase: "Regular Season",
                slotId: assignment.slotId ?? "",
                gameDate: assignment.gameDate ?? "",
                startTime: assignment.startTime ?? "",
                homeTeamId: assignment.homeTeamId ?? "",
                awayTeamId: assignment.awayTeamId ?? "");

            if (map.ContainsKey(key)) continue;

            if (assignment.isExternalOffer)
            {
                map[key] = new
                {
                    source = "preview_repair_external_offer_v1",
                    phase = "Regular Season",
                    outcome = "external-offer",
                    note = "Guest/external offer in preview snapshot after repair."
                };
                continue;
            }

            map[key] = new
            {
                source = "preview_repair_snapshot_fallback_v1",
                phase = "Regular Season",
                outcome = "assigned",
                note = movedBeforeKeys.Contains(key)
                    ? "Game was part of a preview repair change; original engine trace was replaced."
                    : "Fallback preview explanation after repair revalidation (engine candidate trace unavailable for this snapshot)."
            };
        }

        return map;
    }

    private static object BuildPreviewRepairMovedTrace(object? previousTrace, PreviewMoveChange move)
    {
        return new
        {
            source = "preview_repair_move_v1",
            outcome = "assigned",
            movedFrom = new
            {
                slotId = move.FromSlotId,
                gameDate = move.FromGameDate,
                startTime = move.FromStartTime,
                endTime = move.FromEndTime,
                fieldKey = move.FromFieldKey
            },
            movedTo = new
            {
                slotId = move.ToSlotId,
                gameDate = move.ToGameDate,
                startTime = move.ToStartTime,
                endTime = move.ToEndTime,
                fieldKey = move.ToFieldKey
            },
            originalTrace = previousTrace,
            note = "Preview repair moved this game. Original engine trace (if present) is preserved in originalTrace."
        };
    }

    private static Dictionary<string, object> ReadObjectMap(object? value)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (value is null) return result;

        if (value is JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in el.EnumerateObject())
            {
                result[prop.Name] = prop.Value.Clone();
            }
            return result;
        }

        if (value is IDictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                result[kvp.Key] = kvp.Value!;
            }
            return result;
        }

        try
        {
            var serialized = JsonSerializer.Serialize(value);
            using var doc = JsonDocument.Parse(serialized);
            return ReadObjectMap(doc.RootElement);
        }
        catch
        {
            return result;
        }
    }

    private static List<MatchupPair> ParsePreviewUnassignedMatchups(List<object> rows, string phase)
    {
        var list = new List<MatchupPair>();
        foreach (var row in rows ?? new List<object>())
        {
            var phaseValue = GetIssuePhaseFromObject(row);
            if (!string.Equals(phaseValue, phase, StringComparison.OrdinalIgnoreCase))
                continue;

            var home = ReadJsonObjectString(row, "homeTeamId");
            var away = ReadJsonObjectString(row, "awayTeamId");
            if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
                continue;
            list.Add(new MatchupPair(home, away));
        }
        return list;
    }

    private static ScheduleAssignment ToScheduleAssignment(WizardSlotDto slot)
        => new(slot.slotId ?? "", slot.gameDate ?? "", slot.startTime ?? "", slot.endTime ?? "", slot.fieldKey ?? "", slot.homeTeamId ?? "", slot.awayTeamId ?? "", slot.isExternalOffer);

    private static bool TryExtractMoveChanges(ScheduleRepairProposal proposal, out List<PreviewMoveChange> moves)
    {
        moves = new List<PreviewMoveChange>();
        foreach (var change in (proposal.Changes ?? Array.Empty<ScheduleDiffChange>())
            .Where(c => string.Equals(c.ChangeType, "move", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(change.FromSlotId) || string.IsNullOrWhiteSpace(change.ToSlotId))
                return false;

            var beforeGameDate = ReadJsonObjectString(change.Before, "gameDate");
            var beforeStart = ReadJsonObjectString(change.Before, "startTime");
            var beforeEnd = ReadJsonObjectString(change.Before, "endTime");
            var beforeField = ReadJsonObjectString(change.Before, "fieldKey");
            var beforeHome = ReadJsonObjectString(change.Before, "homeTeamId");
            var beforeAway = ReadJsonObjectString(change.Before, "awayTeamId");

            var afterGameDate = ReadJsonObjectString(change.After, "gameDate");
            var afterStart = ReadJsonObjectString(change.After, "startTime");
            var afterEnd = ReadJsonObjectString(change.After, "endTime");
            var afterField = ReadJsonObjectString(change.After, "fieldKey");

            if (string.IsNullOrWhiteSpace(beforeGameDate) || string.IsNullOrWhiteSpace(beforeStart) || string.IsNullOrWhiteSpace(beforeEnd) || string.IsNullOrWhiteSpace(beforeField))
                return false;
            if (string.IsNullOrWhiteSpace(afterGameDate) || string.IsNullOrWhiteSpace(afterStart) || string.IsNullOrWhiteSpace(afterEnd) || string.IsNullOrWhiteSpace(afterField))
                return false;

            moves.Add(new PreviewMoveChange(
                FromSlotId: change.FromSlotId!,
                FromGameDate: beforeGameDate,
                FromStartTime: beforeStart,
                FromEndTime: beforeEnd,
                FromFieldKey: beforeField,
                BeforeHomeTeamId: beforeHome,
                BeforeAwayTeamId: beforeAway,
                ToSlotId: change.ToSlotId!,
                ToGameDate: afterGameDate,
                ToStartTime: afterStart,
                ToEndTime: afterEnd,
                ToFieldKey: afterField));
        }

        return moves.Count > 0;
    }

    private static string ReadJsonObjectString(object? value, string propertyName)
    {
        if (value is null) return "";
        if (value is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(propertyName, out var prop))
                return prop.ValueKind == JsonValueKind.String ? (prop.GetString() ?? "") : prop.ToString();
            return "";
        }
        try
        {
            var serialized = JsonSerializer.Serialize(value);
            using var doc = JsonDocument.Parse(serialized);
            return ReadJsonObjectString(doc.RootElement, propertyName);
        }
        catch
        {
            return "";
        }
    }

    private readonly record struct PreviewMoveChange(
        string FromSlotId,
        string FromGameDate,
        string FromStartTime,
        string FromEndTime,
        string FromFieldKey,
        string BeforeHomeTeamId,
        string BeforeAwayTeamId,
        string ToSlotId,
        string ToGameDate,
        string ToStartTime,
        string ToEndTime,
        string ToFieldKey);

    private record PhaseAssignments(
        List<ScheduleAssignment> Assignments,
        List<ScheduleAssignment> UnassignedSlots,
        List<MatchupPair> UnassignedMatchups,
        List<SchedulePlacementTrace>? PlacementTraces = null
    );

    private record SlotPlanConfig(string SlotType, int? PriorityRank, string? StartTime, string? EndTime);
    private record BlockedDateRange(DateOnly StartDate, DateOnly EndDate, string Label);

    private record GuestAnchor(
        DayOfWeek DayOfWeek,
        string StartTime,
        string EndTime,
        string FieldKey
    );

    private record GuestAnchorSet(
        GuestAnchor? Primary,
        GuestAnchor? Secondary
    );

    private record AnchoredExternalBuildResult(
        List<ScheduleAssignment> Assignments,
        List<ScheduleAssignment> UnassignedSlots
    );

    private static PhaseAssignments AssignPhaseSlots(
        string phase,
        List<SlotInfo> slots,
        List<MatchupPair> matchups,
        List<string> teams,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        int externalOfferPerWeek,
        List<DayOfWeek> preferredDays,
        bool strictPreferredWeeknights,
        GuestAnchorSet? guestAnchors,
        bool scheduleBackward)
    {
        if (slots.Count == 0)
            return new PhaseAssignments(new List<ScheduleAssignment>(), new List<ScheduleAssignment>(), new List<MatchupPair>(matchups));

        if (strictPreferredWeeknights && preferredDays.Count > 0)
        {
            slots = slots.Where(s => IsPreferredDay(s.gameDate, preferredDays)).ToList();
            if (slots.Count == 0)
                return new PhaseAssignments(new List<ScheduleAssignment>(), new List<ScheduleAssignment>(), new List<MatchupPair>(matchups));
        }

        var anchoredExternalSlots = SelectAnchoredExternalSlots(slots, externalOfferPerWeek, guestAnchors);
        if (anchoredExternalSlots.Count > 0)
        {
            var reservedIds = new HashSet<string>(anchoredExternalSlots.Select(s => s.slotId), StringComparer.OrdinalIgnoreCase);
            slots = slots.Where(s => !reservedIds.Contains(s.slotId)).ToList();
        }

        var orderedSlots = OrderSlotsByPreference(slots, preferredDays, scheduleBackward);

        var constraints = new ScheduleConstraints(maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, 0);
        var includePlacementTraces = string.Equals(phase, "Regular Season", StringComparison.OrdinalIgnoreCase);
        var result = ScheduleEngine.AssignMatchups(orderedSlots, matchups, teams, constraints, includePlacementTraces: includePlacementTraces);
        var assignments = new List<ScheduleAssignment>(result.Assignments);
        var carriedUnassignedSlots = new List<ScheduleAssignment>(result.UnassignedSlots);
        if (anchoredExternalSlots.Count > 0)
        {
            var anchoredResult = BuildAnchoredExternalAssignments(assignments, anchoredExternalSlots, teams, maxGamesPerWeek);
            assignments.AddRange(anchoredResult.Assignments);
            carriedUnassignedSlots.AddRange(anchoredResult.UnassignedSlots);
        }
        if (externalOfferPerWeek <= 0 || carriedUnassignedSlots.Count == 0)
            return new PhaseAssignments(assignments, carriedUnassignedSlots, result.UnassignedMatchups, result.PlacementTraces);

        var withExternal = AddExternalOffers(assignments, carriedUnassignedSlots, result.UnassignedMatchups, teams, externalOfferPerWeek, guestAnchors, maxGamesPerWeek);
        return new PhaseAssignments(withExternal.Assignments, withExternal.UnassignedSlots, withExternal.UnassignedMatchups, result.PlacementTraces);
    }

    private static List<SlotInfo> SelectAnchoredExternalSlots(
        List<SlotInfo> slots,
        int externalOfferPerWeek,
        GuestAnchorSet? guestAnchors)
    {
        if (externalOfferPerWeek <= 0 || guestAnchors is null || slots.Count == 0)
            return new List<SlotInfo>();

        var picked = new List<SlotInfo>();
        foreach (var weekGroup in slots
            .GroupBy(s => WeekKey(s.gameDate))
            .OrderBy(g => g.Key))
        {
            if (string.IsNullOrWhiteSpace(weekGroup.Key))
                continue;

            var matches = weekGroup
                .Select(slot => new { slot, score = GuestAnchorScore(slot, guestAnchors) })
                .Where(x => x.score < 100)
                .OrderBy(x => x.score)
                .ThenBy(x => x.slot.gameDate)
                .ThenBy(x => x.slot.startTime)
                .ThenBy(x => x.slot.fieldKey)
                .Take(externalOfferPerWeek)
                .Select(x => x.slot)
                .ToList();
            picked.AddRange(matches);
        }

        return picked;
    }

    private static AnchoredExternalBuildResult BuildAnchoredExternalAssignments(
        List<ScheduleAssignment> existingAssignments,
        IReadOnlyList<SlotInfo> anchoredExternalSlots,
        IReadOnlyList<string> teams,
        int? maxGamesPerWeek)
    {
        if (anchoredExternalSlots.Count == 0 || teams.Count == 0)
            return new AnchoredExternalBuildResult(new List<ScheduleAssignment>(), new List<ScheduleAssignment>());

        var totalCounts = teams
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var homeCounts = teams
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var externalCounts = teams
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var weekCounts = BuildTeamWeekCounts(existingAssignments);

        foreach (var existing in existingAssignments)
        {
            IncrementTeamCount(totalCounts, existing.HomeTeamId);
            IncrementTeamCount(totalCounts, existing.AwayTeamId);
            IncrementTeamCount(homeCounts, existing.HomeTeamId);
            if (existing.IsExternalOffer)
            {
                IncrementTeamCount(externalCounts, existing.HomeTeamId);
            }
        }

        var anchoredAssignments = new List<ScheduleAssignment>();
        var anchoredUnassigned = new List<ScheduleAssignment>();
        foreach (var slot in anchoredExternalSlots
            .OrderBy(s => s.gameDate)
            .ThenBy(s => s.startTime)
            .ThenBy(s => s.fieldKey))
        {
            var weekKey = WeekKey(slot.gameDate);
            var home = PickExternalHomeTeam(teams, totalCounts, homeCounts, externalCounts, weekCounts, weekKey, maxGamesPerWeek);
            if (string.IsNullOrWhiteSpace(home))
            {
                anchoredUnassigned.Add(new ScheduleAssignment(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, "", "", false));
                continue;
            }

            IncrementTeamCount(totalCounts, home);
            IncrementTeamCount(homeCounts, home);
            IncrementTeamCount(externalCounts, home);
            IncrementTeamWeekCount(weekCounts, home, weekKey);
            anchoredAssignments.Add(new ScheduleAssignment(
                SlotId: slot.slotId,
                GameDate: slot.gameDate,
                StartTime: slot.startTime,
                EndTime: slot.endTime,
                FieldKey: slot.fieldKey,
                HomeTeamId: home,
                AwayTeamId: "",
                IsExternalOffer: true
            ));
        }

        return new AnchoredExternalBuildResult(anchoredAssignments, anchoredUnassigned);
    }

    private static PhaseAssignments AssignBracketSlots(List<SlotInfo> slots, List<MatchupPair> matchups)
    {
        var orderedSlots = slots
            .OrderBy(s => SlotTypeSchedulingPriority(s))
            .ThenBy(s => s.priorityRank.HasValue ? 0 : 1)
            .ThenBy(s => s.priorityRank ?? int.MaxValue)
            .ThenBy(s => s.gameDate)
            .ThenBy(s => s.startTime)
            .ThenBy(s => s.fieldKey)
            .ToList();

        var assignments = new List<ScheduleAssignment>();
        var unassignedSlots = new List<ScheduleAssignment>();
        if (orderedSlots.Count == 0)
            return new PhaseAssignments(assignments, unassignedSlots, new List<MatchupPair>(matchups));

        var remainingMatchups = new List<MatchupPair>(matchups);
        var championshipIndex = remainingMatchups.FindIndex(IsChampionshipMatchup);
        if (championshipIndex < 0)
        {
            var queue = new Queue<MatchupPair>(remainingMatchups);
            foreach (var slot in orderedSlots)
            {
                if (queue.Count == 0)
                {
                    unassignedSlots.Add(new ScheduleAssignment(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, "", "", false));
                    continue;
                }

                var matchup = queue.Dequeue();
                assignments.Add(new ScheduleAssignment(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, matchup.HomeTeamId, matchup.AwayTeamId, false));
            }

            return new PhaseAssignments(assignments, unassignedSlots, queue.ToList());
        }

        var championship = remainingMatchups[championshipIndex];
        remainingMatchups.RemoveAt(championshipIndex);

        // Assign non-championship bracket games first (e.g., semifinals).
        var usedSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var semifinalEndTimes = new List<DateTime>();
        var semiQueue = new Queue<MatchupPair>(remainingMatchups);

        foreach (var slot in orderedSlots)
        {
            if (semiQueue.Count == 0) break;
            var matchup = semiQueue.Dequeue();
            assignments.Add(new ScheduleAssignment(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, matchup.HomeTeamId, matchup.AwayTeamId, false));
            usedSlotIds.Add(slot.slotId);
            if (TryGetSlotDateTime(slot.gameDate, slot.endTime, out var endAt))
            {
                semifinalEndTimes.Add(endAt);
            }
            else if (TryGetSlotDateTime(slot.gameDate, slot.startTime, out var startAt))
            {
                semifinalEndTimes.Add(startAt);
            }
        }

        var leftoverSemis = semiQueue.ToList();
        if (leftoverSemis.Count > 0)
        {
            // If semifinal games are still unassigned, championship cannot be scheduled.
            var pending = new List<MatchupPair>(leftoverSemis) { championship };
            foreach (var slot in orderedSlots.Where(s => !usedSlotIds.Contains(s.slotId)))
            {
                unassignedSlots.Add(new ScheduleAssignment(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, "", "", false));
            }
            return new PhaseAssignments(assignments, unassignedSlots, pending);
        }

        var earliestChampionshipStart = semifinalEndTimes.Count > 0 ? semifinalEndTimes.Max() : DateTime.MinValue;
        SlotInfo? championshipSlot = null;
        foreach (var slot in orderedSlots.Where(s => !usedSlotIds.Contains(s.slotId)))
        {
            if (!TryGetSlotDateTime(slot.gameDate, slot.startTime, out var slotStart)) continue;
            if (slotStart < earliestChampionshipStart) continue;
            championshipSlot = slot;
            break;
        }

        if (championshipSlot is not null)
        {
            assignments.Add(new ScheduleAssignment(
                championshipSlot.slotId,
                championshipSlot.gameDate,
                championshipSlot.startTime,
                championshipSlot.endTime,
                championshipSlot.fieldKey,
                championship.HomeTeamId,
                championship.AwayTeamId,
                false));
            usedSlotIds.Add(championshipSlot.slotId);
        }

        foreach (var slot in orderedSlots.Where(s => !usedSlotIds.Contains(s.slotId)))
        {
            unassignedSlots.Add(new ScheduleAssignment(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, "", "", false));
        }

        var unassignedMatchups = new List<MatchupPair>();
        if (championshipSlot is null)
        {
            unassignedMatchups.Add(championship);
        }

        return new PhaseAssignments(assignments, unassignedSlots, unassignedMatchups);
    }

    private static bool IsChampionshipMatchup(MatchupPair matchup)
    {
        var home = matchup.HomeTeamId ?? "";
        var away = matchup.AwayTeamId ?? "";
        return home.StartsWith("Winner", StringComparison.OrdinalIgnoreCase) ||
               away.StartsWith("Winner", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetSlotDateTime(string gameDate, string hhmm, out DateTime value)
    {
        value = default;
        if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        if (!TimeUtil.TryParseMinutes(hhmm, out var minutes))
            return false;
        value = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
        return true;
    }

    private static PhaseAssignments AddExternalOffers(
        List<ScheduleAssignment> assignments,
        List<ScheduleAssignment> unassignedSlots,
        List<MatchupPair> unassignedMatchups,
        IReadOnlyList<string> teams,
        int externalOfferPerWeek,
        GuestAnchorSet? guestAnchors,
        int? maxGamesPerWeek)
    {
        if (externalOfferPerWeek <= 0 || unassignedSlots.Count == 0 || teams.Count == 0)
            return new PhaseAssignments(assignments, unassignedSlots, unassignedMatchups);

        var nextAssignments = new List<ScheduleAssignment>(assignments);
        var remainingSlots = new List<ScheduleAssignment>();

        var totalCounts = teams
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var homeCounts = teams
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var externalCounts = teams
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var weekCounts = BuildTeamWeekCounts(assignments);

        foreach (var existing in assignments)
        {
            IncrementTeamCount(totalCounts, existing.HomeTeamId);
            IncrementTeamCount(totalCounts, existing.AwayTeamId);
            IncrementTeamCount(homeCounts, existing.HomeTeamId);
            if (existing.IsExternalOffer)
            {
                IncrementTeamCount(externalCounts, existing.HomeTeamId);
            }
        }

        var byWeek = unassignedSlots
            .GroupBy(s => WeekKey(s.GameDate))
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var weekGroup in byWeek)
        {
            if (string.IsNullOrWhiteSpace(weekGroup.Key))
            {
                remainingSlots.AddRange(weekGroup);
                continue;
            }

            var existingExternalCount = nextAssignments
                .Where(a => a.IsExternalOffer && string.Equals(WeekKey(a.GameDate), weekGroup.Key, StringComparison.OrdinalIgnoreCase))
                .Count();
            var remainingCapacity = Math.Max(0, externalOfferPerWeek - existingExternalCount);
            if (remainingCapacity <= 0)
            {
                remainingSlots.AddRange(weekGroup);
                continue;
            }

            var ordered = OrderGuestCandidates(weekGroup.ToList(), guestAnchors);
            var picks = ordered.Take(remainingCapacity).ToList();
            var pickIds = new HashSet<string>(picks.Select(p => p.SlotId), StringComparer.OrdinalIgnoreCase);

            foreach (var slot in weekGroup)
            {
                if (!pickIds.Contains(slot.SlotId))
                {
                    remainingSlots.Add(slot);
                    continue;
                }

                var home = PickExternalHomeTeam(teams, totalCounts, homeCounts, externalCounts, weekCounts, weekGroup.Key, maxGamesPerWeek);
                if (string.IsNullOrWhiteSpace(home))
                {
                    remainingSlots.Add(slot);
                    continue;
                }

                IncrementTeamCount(totalCounts, home);
                IncrementTeamCount(homeCounts, home);
                IncrementTeamCount(externalCounts, home);
                IncrementTeamWeekCount(weekCounts, home, weekGroup.Key);
                nextAssignments.Add(slot with
                {
                    HomeTeamId = home,
                    AwayTeamId = "",
                    IsExternalOffer = true
                });
            }
        }

        return new PhaseAssignments(nextAssignments, remainingSlots, unassignedMatchups);
    }

    private static List<ScheduleAssignment> OrderGuestCandidates(List<ScheduleAssignment> slots, GuestAnchorSet? guestAnchors)
    {
        return slots
            .OrderBy(s => GuestAnchorScore(s, guestAnchors))
            .ThenBy(s => s.GameDate)
            .ThenBy(s => s.StartTime)
            .ThenBy(s => s.FieldKey)
            .ToList();
    }

    private static int GuestAnchorScore(ScheduleAssignment slot, GuestAnchorSet? guestAnchors)
    {
        if (guestAnchors is null) return 100;

        if (MatchesGuestAnchor(slot, guestAnchors.Primary, strictField: true)) return 0;
        if (MatchesGuestAnchor(slot, guestAnchors.Secondary, strictField: true)) return 1;
        if (MatchesGuestAnchor(slot, guestAnchors.Primary, strictField: false)) return 2;
        if (MatchesGuestAnchor(slot, guestAnchors.Secondary, strictField: false)) return 3;
        return 100;
    }

    private static int GuestAnchorScore(SlotInfo slot, GuestAnchorSet? guestAnchors)
    {
        if (guestAnchors is null) return 100;

        if (MatchesGuestAnchor(slot, guestAnchors.Primary, strictField: true)) return 0;
        if (MatchesGuestAnchor(slot, guestAnchors.Secondary, strictField: true)) return 1;
        if (MatchesGuestAnchor(slot, guestAnchors.Primary, strictField: false)) return 2;
        if (MatchesGuestAnchor(slot, guestAnchors.Secondary, strictField: false)) return 3;
        return 100;
    }

    private static bool MatchesGuestAnchor(ScheduleAssignment slot, GuestAnchor? anchor, bool strictField)
    {
        if (anchor is null) return false;
        if (!DateTime.TryParseExact(slot.GameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return false;
        if (dt.DayOfWeek != anchor.DayOfWeek) return false;
        if (!string.Equals(slot.StartTime, anchor.StartTime, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(slot.EndTime, anchor.EndTime, StringComparison.OrdinalIgnoreCase)) return false;
        if (!strictField) return true;
        return string.Equals(slot.FieldKey, anchor.FieldKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesGuestAnchor(SlotInfo slot, GuestAnchor? anchor, bool strictField)
    {
        if (anchor is null) return false;
        if (!DateTime.TryParseExact(slot.gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return false;
        if (dt.DayOfWeek != anchor.DayOfWeek) return false;
        if (!string.Equals(slot.startTime, anchor.StartTime, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(slot.endTime, anchor.EndTime, StringComparison.OrdinalIgnoreCase)) return false;
        if (!strictField) return true;
        return string.Equals(slot.fieldKey, anchor.FieldKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string PickExternalHomeTeam(
        IReadOnlyList<string> teams,
        Dictionary<string, int> totalCounts,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> externalCounts,
        Dictionary<string, int> weekCounts,
        string weekKey,
        int? maxGamesPerWeek)
    {
        return teams
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => CanAssignExternalInWeek(t, weekCounts, weekKey, maxGamesPerWeek))
            .OrderBy(t => externalCounts.TryGetValue(t, out var external) ? external : int.MaxValue)
            .ThenBy(t => totalCounts.TryGetValue(t, out var total) ? total : int.MaxValue)
            .ThenBy(t => homeCounts.TryGetValue(t, out var home) ? home : int.MaxValue)
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "";
    }

    private static void IncrementTeamCount(Dictionary<string, int> counts, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return;
        if (!counts.ContainsKey(teamId)) return;
        counts[teamId] += 1;
    }

    private static Dictionary<string, int> BuildTeamWeekCounts(IEnumerable<ScheduleAssignment> assignments)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments)
        {
            var weekKey = WeekKey(assignment.GameDate);
            if (string.IsNullOrWhiteSpace(weekKey)) continue;
            IncrementTeamWeekCount(counts, assignment.HomeTeamId, weekKey);
            IncrementTeamWeekCount(counts, assignment.AwayTeamId, weekKey);
        }
        return counts;
    }

    private static bool CanAssignExternalInWeek(
        string teamId,
        Dictionary<string, int> weekCounts,
        string weekKey,
        int? maxGamesPerWeek)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return false;
        if (!maxGamesPerWeek.HasValue || maxGamesPerWeek.Value <= 0) return true;
        if (string.IsNullOrWhiteSpace(weekKey)) return true;
        return GetTeamWeekCount(weekCounts, teamId, weekKey) < maxGamesPerWeek.Value;
    }

    private static int GetTeamWeekCount(Dictionary<string, int> counts, string teamId, string weekKey)
    {
        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(weekKey)) return 0;
        var key = $"{teamId}|{weekKey}";
        return counts.TryGetValue(key, out var count) ? count : 0;
    }

    private static void IncrementTeamWeekCount(Dictionary<string, int> counts, string teamId, string weekKey)
    {
        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(weekKey)) return;
        var key = $"{teamId}|{weekKey}";
        counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
    }

    private static string WeekKey(string gameDate)
    {
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return "";
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{dt.Year}-W{week:D2}";
    }

    private static WizardSlotDto ToWizardSlot(string phase, ScheduleAssignment assignment)
        => new(
            phase,
            assignment.SlotId,
            assignment.GameDate,
            assignment.StartTime,
            assignment.EndTime,
            assignment.FieldKey,
            assignment.HomeTeamId,
            assignment.AwayTeamId,
            assignment.IsExternalOffer
        );

    private static List<SlotInfo> FilterSlots(List<SlotInfo> slots, DateOnly from, DateOnly to)
    {
        if (from > to) return new List<SlotInfo>();
        return slots.Where(s => IsWithinDateRange(s.gameDate, from, to))
            .OrderBy(s => s.gameDate)
            .ThenBy(s => s.startTime)
            .ThenBy(s => s.fieldKey)
            .ToList();
    }

    private static bool IsWithinDateRange(string gameDate, DateOnly from, DateOnly to)
    {
        if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", out var date)) return false;
        return date >= from && date <= to;
    }

    private static List<BlockedDateRange> NormalizeBlockedDateRanges(List<DateRangeOption>? ranges)
    {
        var normalized = new List<BlockedDateRange>();
        if (ranges is null) return normalized;

        var index = 0;
        foreach (var raw in ranges)
        {
            var startRaw = (raw.startDate ?? "").Trim();
            var endRaw = (raw.endDate ?? "").Trim();
            var label = (raw.label ?? "").Trim();
            if (string.IsNullOrWhiteSpace(startRaw) || string.IsNullOrWhiteSpace(endRaw))
            {
                index++;
                continue;
            }

            if (!DateOnly.TryParseExact(startRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, $"blockedDateRanges[{index}].startDate must be YYYY-MM-DD.");
            if (!DateOnly.TryParseExact(endRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, $"blockedDateRanges[{index}].endDate must be YYYY-MM-DD.");
            if (end < start)
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, $"blockedDateRanges[{index}] endDate must be on or after startDate.");

            normalized.Add(new BlockedDateRange(start, end, label));
            index++;
        }

        return normalized;
    }

    private static List<SlotInfo> ApplyDateBlackouts(List<SlotInfo> slots, List<BlockedDateRange> blockedRanges)
    {
        if (blockedRanges.Count == 0) return slots;
        return slots.Where(slot => !IsInBlockedRange(slot.gameDate, blockedRanges)).ToList();
    }

    private static bool IsInBlockedRange(string gameDate, List<BlockedDateRange> blockedRanges)
    {
        if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        foreach (var range in blockedRanges)
        {
            if (date >= range.StartDate && date <= range.EndDate)
                return true;
        }

        return false;
    }

    private static List<ScheduleSlot> OrderSlotsByPreference(List<SlotInfo> slots, List<DayOfWeek> preferredDays, bool scheduleBackward)
    {
        var ordered = slots
            .OrderBy(s => SlotTypeSchedulingPriority(s))
            .ThenBy(s => s.priorityRank.HasValue ? 0 : 1)
            .ThenBy(s => s.priorityRank ?? int.MaxValue)
            .ThenBy(s => PreferredDayRank(s.gameDate, preferredDays));

        ordered = scheduleBackward
            ? ordered.ThenByDescending(s => s.gameDate).ThenByDescending(s => s.startTime).ThenBy(s => s.fieldKey)
            : ordered.ThenBy(s => s.gameDate).ThenBy(s => s.startTime).ThenBy(s => s.fieldKey);

        return ordered
            .Select(s => new ScheduleSlot(s.slotId, s.gameDate, s.startTime, s.endTime, s.fieldKey, s.offeringTeamId))
            .ToList();
    }

    private static int PreferredDayRank(string gameDate, List<DayOfWeek> preferredDays)
    {
        if (preferredDays.Count == 0) return int.MaxValue;
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return int.MaxValue;
        var index = preferredDays.IndexOf(dt.DayOfWeek);
        return index >= 0 ? index : int.MaxValue;
    }

    private static bool IsPreferredDay(string gameDate, List<DayOfWeek> preferredDays)
    {
        if (preferredDays.Count == 0) return true;
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return false;
        return preferredDays.Contains(dt.DayOfWeek);
    }

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

        return ordered.Distinct().Take(3).ToList();
    }

    private static Dictionary<string, SlotPlanConfig> BuildSlotPlanLookup(List<SlotPlanItem>? slotPlan)
    {
        var result = new Dictionary<string, SlotPlanConfig>(StringComparer.OrdinalIgnoreCase);
        if (slotPlan is null) return result;

        foreach (var item in slotPlan)
        {
            var slotId = (item.slotId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(slotId)) continue;

            var slotType = NormalizeSlotType(item.slotType);
            var priority = item.priorityRank.HasValue && item.priorityRank.Value > 0 ? item.priorityRank.Value : (int?)null;
            var startTime = NormalizeTime(item.startTime);
            var endTime = NormalizeTime(item.endTime);
            result[slotId] = new SlotPlanConfig(slotType, priority, startTime, endTime);
        }

        return result;
    }

    private static List<SlotInfo> ApplySlotPlan(List<SlotInfo> slots, Dictionary<string, SlotPlanConfig> slotPlanLookup, bool hasSlotPlan)
    {
        var planned = new List<SlotInfo>(slots.Count);
        foreach (var slot in slots)
        {
            SlotPlanConfig config;
            if (slotPlanLookup.TryGetValue(slot.slotId, out var matched))
            {
                config = matched;
            }
            else
            {
                config = hasSlotPlan
                    ? new SlotPlanConfig("practice", null, null, null)
                    : new SlotPlanConfig("game", null, null, null);
            }

            var normalizedType = NormalizeSlotType(config.SlotType);
            var rank = normalizedType == "practice" ? null : config.PriorityRank;
            var startTime = string.IsNullOrWhiteSpace(config.StartTime) ? slot.startTime : config.StartTime;
            var endTime = string.IsNullOrWhiteSpace(config.EndTime) ? slot.endTime : config.EndTime;
            if (!TimeUtil.IsValidRange(startTime, endTime, out _, out _, out _))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, $"Invalid slot timing for slotId {slot.slotId}. startTime must be before endTime and use HH:MM.");
            }
            planned.Add(slot with
            {
                slotType = normalizedType,
                priorityRank = rank,
                startTime = startTime,
                endTime = endTime
            });
        }
        return planned;
    }

    private static string? NormalizeTime(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!TimeUtil.TryParseMinutes(value, out var minutes)) return null;
        var h = minutes / 60;
        var m = minutes % 60;
        return $"{h:D2}:{m:D2}";
    }

    private static string NormalizeSlotType(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant();
        if (key == "both") return "both";
        if (key == "game") return "game";
        return "practice";
    }

    private static bool IsGameCapableSlotType(SlotInfo slot)
    {
        return slot.slotType == "game" || slot.slotType == "both";
    }

    private static int SlotTypeSchedulingPriority(SlotInfo slot)
    {
        if (slot.slotType == "practice") return 1;
        return 0;
    }

    private static GuestAnchorSet? NormalizeGuestAnchors(GuestAnchorOption? primary, GuestAnchorOption? secondary)
    {
        var normalizedPrimary = NormalizeGuestAnchor(primary);
        var normalizedSecondary = NormalizeGuestAnchor(secondary);
        if (normalizedPrimary is null && normalizedSecondary is null) return null;
        return new GuestAnchorSet(normalizedPrimary, normalizedSecondary);
    }

    private static GuestAnchor? NormalizeGuestAnchor(GuestAnchorOption? option)
    {
        if (option is null) return null;
        var day = ParseDayOfWeek(option.dayOfWeek);
        var start = (option.startTime ?? "").Trim();
        var end = (option.endTime ?? "").Trim();
        var fieldKey = (option.fieldKey ?? "").Trim();
        if (!day.HasValue) return null;
        if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end) || string.IsNullOrWhiteSpace(fieldKey)) return null;
        return new GuestAnchor(day.Value, start, end, fieldKey);
    }

    private static DayOfWeek? ParseDayOfWeek(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant();
        if (key.StartsWith("sun")) return DayOfWeek.Sunday;
        if (key.StartsWith("mon")) return DayOfWeek.Monday;
        if (key.StartsWith("tue")) return DayOfWeek.Tuesday;
        if (key.StartsWith("wed")) return DayOfWeek.Wednesday;
        if (key.StartsWith("thu")) return DayOfWeek.Thursday;
        if (key.StartsWith("fri")) return DayOfWeek.Friday;
        if (key.StartsWith("sat")) return DayOfWeek.Saturday;
        return null;
    }

    private static List<MatchupPair> BuildRepeatedMatchups(IReadOnlyList<string> teams, int gamesPerTeam)
    {
        if (teams.Count < 2) return new List<MatchupPair>();
        if (gamesPerTeam <= 0) return new List<MatchupPair>();

        var roundGames = Math.Max(1, teams.Count - 1);
        var fullRounds = gamesPerTeam / roundGames;
        var remainderGames = gamesPerTeam % roundGames;

        // If games/team is not an exact multiple of full round-robin cycles,
        // use target-based generation so we do not round up and over-schedule.
        if (remainderGames > 0)
        {
            return BuildTargetMatchups(teams, gamesPerTeam);
        }

        var result = new List<MatchupPair>();

        for (var round = 0; round < fullRounds; round++)
        {
            var matchups = ScheduleEngine.BuildRoundRobin(teams);
            if (round % 2 == 1)
            {
                matchups = matchups.Select(m => new MatchupPair(m.AwayTeamId, m.HomeTeamId)).ToList();
            }
            result.AddRange(matchups);
        }

        return result;
    }

    private static List<MatchupPair> BuildTargetMatchups(IReadOnlyList<string> teams, int gamesPerTeam)
    {
        if (teams.Count < 2) return new List<MatchupPair>();
        if (gamesPerTeam <= 0) return new List<MatchupPair>();

        var counts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var matchups = new List<MatchupPair>();

        var rounds = Math.Max(1, gamesPerTeam);
        for (var round = 0; round < rounds; round++)
        {
            var roundMatchups = ScheduleEngine.BuildRoundRobin(teams);
            if (round % 2 == 1)
            {
                roundMatchups = roundMatchups.Select(m => new MatchupPair(m.AwayTeamId, m.HomeTeamId)).ToList();
            }

            foreach (var m in roundMatchups)
            {
                if (counts[m.HomeTeamId] >= gamesPerTeam || counts[m.AwayTeamId] >= gamesPerTeam)
                    continue;

                matchups.Add(m);
                counts[m.HomeTeamId] += 1;
                counts[m.AwayTeamId] += 1;

                if (counts.Values.All(v => v >= gamesPerTeam))
                    return matchups;
            }
        }

        return matchups;
    }

    private static List<MatchupPair> BuildBracketMatchups()
    {
        return new List<MatchupPair>
        {
            new MatchupPair("Seed1", "Seed4"),
            new MatchupPair("Seed2", "Seed3"),
            new MatchupPair("WinnerSF1", "WinnerSF2")
        };
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

    private async Task<List<SlotInfo>> LoadAvailabilitySlotsAsync(string leagueId, string division, DateOnly dateFrom, DateOnly dateTo)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
        var pk = Constants.Pk.Slots(leagueId, division);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}' and Status eq '{ApiGuards.EscapeOData(Constants.Status.SlotOpen)}'";
        filter += $" and GameDate ge '{ApiGuards.EscapeOData(dateFrom.ToString("yyyy-MM-dd"))}'";
        filter += $" and GameDate le '{ApiGuards.EscapeOData(dateTo.ToString("yyyy-MM-dd"))}'";

        var list = new List<SlotInfo>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            if (!SlotEntityUtil.IsAvailability(e)) continue;

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
                offeringTeamId: SlotEntityUtil.ReadString(e, "OfferingTeamId"),
                slotType: "game",
                priorityRank: null
            ));
        }

        return list
            .Where(s => !string.IsNullOrWhiteSpace(s.gameDate))
            .OrderBy(s => s.gameDate)
            .ThenBy(s => s.startTime)
            .ThenBy(s => s.fieldKey)
            .ToList();
    }

    private async Task ApplyAssignmentsAsync(string leagueId, string division, string runId, IEnumerable<WizardSlotDto> assignments)
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

            var nowUtc = DateTimeOffset.UtcNow;

            SlotEntityUtil.ApplySchedulerAssignment(
                slot,
                runId,
                a.homeTeamId ?? "",
                a.awayTeamId ?? "",
                a.isExternalOffer,
                confirmedBy: "Wizard",
                nowUtc: nowUtc);

            slot["GameDate"] = a.gameDate ?? (slot.GetString("GameDate") ?? "");
            slot["StartTime"] = a.startTime ?? (slot.GetString("StartTime") ?? "");
            slot["EndTime"] = a.endTime ?? (slot.GetString("EndTime") ?? "");
            if (TimeUtil.TryParseMinutes(a.startTime ?? "", out var startMin))
            {
                slot["StartMin"] = startMin;
            }
            if (TimeUtil.TryParseMinutes(a.endTime ?? "", out var endMin))
            {
                slot["EndMin"] = endMin;
            }

            var notes = (slot.GetString("Notes") ?? "").Trim();
            var phaseNote = $"Wizard: {a.phase}";
            if (string.IsNullOrWhiteSpace(notes))
                slot["Notes"] = phaseNote;
            else if (!notes.Contains(phaseNote, StringComparison.OrdinalIgnoreCase))
                slot["Notes"] = $"{notes} | {phaseNote}";
            await table.UpdateEntityAsync(slot, slot.ETag, TableUpdateMode.Merge);
        }
    }

    private async Task SaveWizardRunAsync(string leagueId, string division, string runId, string createdBy, WizardSummary summary, WizardRequest request)
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
            ["DateFrom"] = request.seasonStart ?? "",
            ["DateTo"] = request.seasonEnd ?? "",
            ["MaxGamesPerWeek"] = request.maxGamesPerWeek ?? 0,
            ["NoDoubleHeaders"] = request.noDoubleHeaders ?? true,
            ["BalanceHomeAway"] = request.balanceHomeAway ?? true,
            ["ExternalOfferPerWeek"] = request.externalOfferPerWeek ?? 0,
            ["BlockedDateRanges"] = System.Text.Json.JsonSerializer.Serialize(request.blockedDateRanges ?? new List<DateRangeOption>()),
            ["ConstructionStrategy"] = NormalizeConstructionStrategy(request.constructionStrategy),
            ["Seed"] = request.seed ?? 0,
            ["Summary"] = System.Text.Json.JsonSerializer.Serialize(summary)
        };

        await table.AddEntityAsync(entity);
    }

    private record SlotInfo(
        string slotId,
        string gameDate,
        string startTime,
        string endTime,
        string fieldKey,
        string offeringTeamId,
        string slotType,
        int? priorityRank);
}
