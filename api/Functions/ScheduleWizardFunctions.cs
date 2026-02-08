using System.Globalization;
using System.Net;
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
        GuestAnchorOption? guestAnchorSecondary
    );

    public record SlotPlanItem(
        string? slotId,
        string? slotType,
        int? priorityRank
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
        int totalIssues
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
            var poolGamesPerTeam = Math.Max(0, body.poolGamesPerTeam ?? 1);
            var externalOfferPerWeek = Math.Max(0, body.externalOfferPerWeek ?? 0);
            var maxGamesPerWeek = (body.maxGamesPerWeek ?? 0) <= 0 ? (int?)null : body.maxGamesPerWeek;
            var noDoubleHeaders = body.noDoubleHeaders ?? true;
            var balanceHomeAway = body.balanceHomeAway ?? true;
            var preferredDays = NormalizePreferredDays(body.preferredWeeknights);
            var strictPreferredWeeknights = body.strictPreferredWeeknights ?? false;

            var teams = await LoadTeamsAsync(leagueId, division);
            if (teams.Count < 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Need at least two teams to schedule.");

            var rawSlots = await LoadAvailabilitySlotsAsync(leagueId, division, seasonStart, bracketEnd ?? seasonEnd);
            if (rawSlots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No availability slots found for this division.");
            var slotPlanLookup = BuildSlotPlanLookup(body.slotPlan);
            var hasSlotPlan = body.slotPlan is not null && body.slotPlan.Count > 0;
            var allSlots = ApplySlotPlan(rawSlots, slotPlanLookup, hasSlotPlan);
            var gameCapableSlots = allSlots.Where(IsGameCapableSlotType).ToList();
            if (gameCapableSlots.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No game-capable slots selected. Mark at least one slot as game or both.");
            var guestAnchors = NormalizeGuestAnchors(body.guestAnchorPrimary, body.guestAnchorSecondary);

            var regularRangeEnd = poolStart.HasValue ? poolStart.Value.AddDays(-1) : seasonEnd;
            var regularSlots = FilterSlots(gameCapableSlots, seasonStart, regularRangeEnd);
            var poolSlots = poolStart.HasValue && poolEnd.HasValue
                ? FilterSlots(gameCapableSlots, poolStart.Value, poolEnd.Value)
                : new List<SlotInfo>();
            var bracketSlots = bracketStart.HasValue && bracketEnd.HasValue
                ? FilterSlots(gameCapableSlots, bracketStart.Value, bracketEnd.Value)
                : new List<SlotInfo>();

            var regularMatchups = BuildRepeatedMatchups(teams, minGamesPerTeam);
            var poolMatchups = BuildTargetMatchups(teams, poolGamesPerTeam);
            var bracketMatchups = BuildBracketMatchups();

            var regularAssignments = AssignPhaseSlots("Regular Season", regularSlots, regularMatchups, teams, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, externalOfferPerWeek, preferredDays, strictPreferredWeeknights, guestAnchors);
            var poolAssignments = AssignPhaseSlots("Pool Play", poolSlots, poolMatchups, teams, null, noDoubleHeaders, balanceHomeAway, 0, preferredDays: new List<DayOfWeek>(), strictPreferredWeeknights: false, guestAnchors: null);
            var bracketAssignments = AssignBracketSlots(bracketSlots, bracketMatchups);

            var summary = new WizardSummary(
                regularSeason: BuildPhaseSummary("Regular Season", regularSlots.Count, regularAssignments.Assignments.Count, regularMatchups.Count, regularAssignments.UnassignedMatchups.Count),
                poolPlay: BuildPhaseSummary("Pool Play", poolSlots.Count, poolAssignments.Assignments.Count, poolMatchups.Count, poolAssignments.UnassignedMatchups.Count),
                bracket: BuildPhaseSummary("Bracket", bracketSlots.Count, bracketAssignments.Assignments.Count, bracketMatchups.Count, bracketAssignments.UnassignedMatchups.Count),
                totalSlots: gameCapableSlots.Count,
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
            var validation = ScheduleValidation.Validate(validationResult, constraints);
            var issues = validation.Issues
                .Select(i => (object)new { ruleId = i.RuleId, severity = i.Severity, message = i.Message, details = i.Details })
                .ToList();

            if (apply)
            {
                var runId = Guid.NewGuid().ToString("N");
                await ApplyAssignmentsAsync(leagueId, division, runId, assignments);
                await SaveWizardRunAsync(leagueId, division, runId, me.Email ?? me.UserId, summary, body);
            }

            UsageTelemetry.Track(_log, apply ? "api_schedule_wizard_apply" : "api_schedule_wizard_preview", leagueId, me.UserId, new
            {
                division,
                slotsTotal = summary.totalSlots,
                assignedTotal = summary.totalAssigned,
                issues = validation.TotalIssues
            });

            return ApiResponses.Ok(req, new WizardPreviewDto(summary, assignments, unassignedSlots, unassignedMatchups, warnings, issues, validation.TotalIssues));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Schedule wizard failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
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

    private record PhaseAssignments(
        List<ScheduleAssignment> Assignments,
        List<ScheduleAssignment> UnassignedSlots,
        List<MatchupPair> UnassignedMatchups
    );

    private record SlotPlanConfig(string SlotType, int? PriorityRank);

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
        GuestAnchorSet? guestAnchors)
    {
        if (slots.Count == 0)
            return new PhaseAssignments(new List<ScheduleAssignment>(), new List<ScheduleAssignment>(), new List<MatchupPair>(matchups));

        if (strictPreferredWeeknights && preferredDays.Count > 0)
        {
            slots = slots.Where(s => IsPreferredDay(s.gameDate, preferredDays)).ToList();
            if (slots.Count == 0)
                return new PhaseAssignments(new List<ScheduleAssignment>(), new List<ScheduleAssignment>(), new List<MatchupPair>(matchups));
        }

        var orderedSlots = OrderSlotsByPreference(slots, preferredDays);

        var constraints = new ScheduleConstraints(maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, 0);
        var result = ScheduleEngine.AssignMatchups(orderedSlots, matchups, teams, constraints);
        if (externalOfferPerWeek <= 0 || result.UnassignedSlots.Count == 0)
            return new PhaseAssignments(result.Assignments, result.UnassignedSlots, result.UnassignedMatchups);

        var withExternal = AddExternalOffers(result.Assignments, result.UnassignedSlots, result.UnassignedMatchups, teams, externalOfferPerWeek, guestAnchors);
        return withExternal;
    }

    private static PhaseAssignments AssignBracketSlots(List<SlotInfo> slots, List<MatchupPair> matchups)
    {
        var assignments = new List<ScheduleAssignment>();
        var unassignedSlots = new List<ScheduleAssignment>();
        var remaining = new Queue<MatchupPair>(matchups);

        foreach (var slot in slots.OrderBy(s => s.gameDate).ThenBy(s => s.startTime))
        {
            if (remaining.Count == 0)
            {
                unassignedSlots.Add(new ScheduleAssignment(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, "", "", false));
                continue;
            }

            var matchup = remaining.Dequeue();
            assignments.Add(new ScheduleAssignment(slot.slotId, slot.gameDate, slot.startTime, slot.endTime, slot.fieldKey, matchup.HomeTeamId, matchup.AwayTeamId, false));
        }

        return new PhaseAssignments(assignments, unassignedSlots, remaining.ToList());
    }

    private static PhaseAssignments AddExternalOffers(
        List<ScheduleAssignment> assignments,
        List<ScheduleAssignment> unassignedSlots,
        List<MatchupPair> unassignedMatchups,
        IReadOnlyList<string> teams,
        int externalOfferPerWeek,
        GuestAnchorSet? guestAnchors)
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

        foreach (var existing in assignments)
        {
            IncrementTeamCount(totalCounts, existing.HomeTeamId);
            IncrementTeamCount(totalCounts, existing.AwayTeamId);
            IncrementTeamCount(homeCounts, existing.HomeTeamId);
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

            var ordered = OrderGuestCandidates(weekGroup.ToList(), guestAnchors);
            var picks = ordered.Take(externalOfferPerWeek).ToList();
            var pickIds = new HashSet<string>(picks.Select(p => p.SlotId), StringComparer.OrdinalIgnoreCase);

            foreach (var slot in weekGroup)
            {
                if (!pickIds.Contains(slot.SlotId))
                {
                    remainingSlots.Add(slot);
                    continue;
                }

                var home = PickExternalHomeTeam(teams, totalCounts, homeCounts);
                if (string.IsNullOrWhiteSpace(home))
                {
                    remainingSlots.Add(slot);
                    continue;
                }

                IncrementTeamCount(totalCounts, home);
                IncrementTeamCount(homeCounts, home);
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

    private static string PickExternalHomeTeam(
        IReadOnlyList<string> teams,
        Dictionary<string, int> totalCounts,
        Dictionary<string, int> homeCounts)
    {
        return teams
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => totalCounts.TryGetValue(t, out var total) ? total : int.MaxValue)
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

    private static List<ScheduleSlot> OrderSlotsByPreference(List<SlotInfo> slots, List<DayOfWeek> preferredDays)
    {
        return slots
            .OrderBy(s => s.priorityRank.HasValue ? 0 : 1)
            .ThenBy(s => s.priorityRank ?? int.MaxValue)
            .ThenBy(s => PreferredDayRank(s.gameDate, preferredDays))
            .ThenBy(s => s.gameDate)
            .ThenBy(s => s.startTime)
            .ThenBy(s => s.fieldKey)
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

        return ordered.Distinct().ToList();
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
            result[slotId] = new SlotPlanConfig(slotType, priority);
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
                    ? new SlotPlanConfig("practice", null)
                    : new SlotPlanConfig("game", null);
            }

            var normalizedType = NormalizeSlotType(config.SlotType);
            var rank = normalizedType == "practice" ? null : config.PriorityRank;
            planned.Add(slot with
            {
                slotType = normalizedType,
                priorityRank = rank
            });
        }
        return planned;
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
        var rounds = (int)Math.Ceiling(gamesPerTeam / (double)roundGames);
        var result = new List<MatchupPair>();

        for (var round = 0; round < rounds; round++)
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
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}' and Status eq '{ApiGuards.EscapeOData(Constants.Status.SlotOpen)}' and IsAvailability eq true";
        filter += $" and GameDate ge '{ApiGuards.EscapeOData(dateFrom.ToString("yyyy-MM-dd"))}'";
        filter += $" and GameDate le '{ApiGuards.EscapeOData(dateTo.ToString("yyyy-MM-dd"))}'";

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
                offeringTeamId: (e.GetString("OfferingTeamId") ?? "").Trim(),
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

            slot["OfferingTeamId"] = a.homeTeamId ?? "";
            slot["HomeTeamId"] = a.homeTeamId ?? "";
            slot["AwayTeamId"] = a.isExternalOffer ? "" : (a.awayTeamId ?? "");
            slot["IsExternalOffer"] = a.isExternalOffer;
            slot["IsAvailability"] = false;
            if (a.isExternalOffer)
            {
                slot["Status"] = Constants.Status.SlotOpen;
                slot["ConfirmedTeamId"] = "";
                slot["ConfirmedRequestId"] = "";
                slot["ConfirmedBy"] = "";
                slot["ConfirmedUtc"] = "";
            }
            else
            {
                slot["Status"] = Constants.Status.SlotConfirmed;
                slot["ConfirmedTeamId"] = a.awayTeamId ?? "";
                slot["ConfirmedBy"] = "Wizard";
                slot["ConfirmedUtc"] = DateTimeOffset.UtcNow;
            }
            slot["ScheduleRunId"] = runId;

            var notes = (slot.GetString("Notes") ?? "").Trim();
            var phaseNote = $"Wizard: {a.phase}";
            if (string.IsNullOrWhiteSpace(notes))
                slot["Notes"] = phaseNote;
            else if (!notes.Contains(phaseNote, StringComparison.OrdinalIgnoreCase))
                slot["Notes"] = $"{notes} | {phaseNote}";

            slot["UpdatedUtc"] = DateTimeOffset.UtcNow;
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
