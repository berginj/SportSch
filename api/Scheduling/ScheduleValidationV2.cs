using System.Globalization;

namespace GameSwap.Functions.Scheduling;

public record ScheduleValidationV2Config(
    int? MaxGamesPerWeek,
    bool NoDoubleHeaders,
    bool BalanceHomeAway,
    IReadOnlyList<ScheduleBlackoutWindow>? BlackoutWindows = null,
    bool TreatUnassignedRequiredMatchupsAsHard = true,
    IReadOnlyDictionary<string, int>? MatchupPriorityByPair = null,
    SchedulePhaseReliabilityWeights? PhaseReliabilityWeights = null,
    IReadOnlyCollection<string>? NoGamesOnDates = null,
    int? NoGamesBeforeMinute = null,
    int? NoGamesAfterMinute = null,
    int? MaxExternalOffersPerTeamSeason = null);

public record SchedulePhaseReliabilityWeights(double Early, double Mid, double Late)
{
    public static SchedulePhaseReliabilityWeights Default => new(0.85, 1.0, 1.2);
}

public record ScheduleRuleViolation(
    string RuleId,
    string Severity,
    string Message,
    IReadOnlyList<string> TeamIds,
    IReadOnlyList<string> SlotIds,
    IReadOnlyList<string> WeekKeys,
    Dictionary<string, object?> Details);

public record ScheduleRuleGroupReport(
    string RuleId,
    string Severity,
    int Count,
    string Summary,
    IReadOnlyList<ScheduleRuleViolation> Violations,
    Dictionary<string, object?> SmallestAffectedSet);

public record ScheduleSoftScoreTerm(string ObjectiveId, int Weight, double Raw, double Weighted);

public record ScheduleRuleHealthReport(
    string Status,
    bool ApplyBlocked,
    int HardViolationCount,
    int SoftViolationCount,
    double SoftScore,
    IReadOnlyList<ScheduleSoftScoreTerm> ScoreBreakdown,
    IReadOnlyList<ScheduleRuleGroupReport> Groups);

public record ScheduleValidationV2Result(
    ScheduleRuleHealthReport RuleHealth,
    IReadOnlyList<ScheduleRuleViolation> Violations);

public static class ScheduleValidationV2
{
    private const string SeverityHard = "hard";
    private const string SeveritySoft = "soft";

    public static ScheduleValidationV2Result Validate(
        ScheduleResult result,
        ScheduleValidationV2Config config,
        IReadOnlyList<string>? teams = null)
    {
        var violations = new List<ScheduleRuleViolation>();

        AddBlackoutViolations(result.Assignments, config.BlackoutWindows ?? Array.Empty<ScheduleBlackoutWindow>(), violations);
        AddMissingOpponentViolations(result.Assignments, violations);
        AddSlotCapacityViolations(result.Assignments, violations);
        AddTeamOverlapViolations(result.Assignments, violations);
        AddDoubleHeaderViolations(result.Assignments, config.NoDoubleHeaders, violations);
        AddMaxGamesPerWeekViolations(result.Assignments, config.MaxGamesPerWeek, violations);
        AddNoGamesOnDatesViolations(result.Assignments, config.NoGamesOnDates, violations);
        AddTimeWindowViolations(result.Assignments, config.NoGamesBeforeMinute, config.NoGamesAfterMinute, violations);
        AddExternalOfferCapViolations(result.Assignments, config.MaxExternalOffersPerTeamSeason, violations);
        AddUnassignedRequiredMatchupViolations(result.UnassignedMatchups, config.TreatUnassignedRequiredMatchupsAsHard, violations);
        AddUnassignedSlotSoftViolations(result.UnassignedSlots, violations);
        AddPairRepeatSoftViolations(result.Assignments, violations);
        AddIdleGapSoftViolations(result.Assignments, teams, violations);
        AddHomeAwayBalanceSoftViolations(result.Assignments, teams, config.BalanceHomeAway, violations);

        var groups = violations
            .GroupBy(v => new RuleGroupKey(v.RuleId, v.Severity))
            .Select(g =>
            {
                var list = g.ToList();
                var teamIds = list.SelectMany(v => v.TeamIds)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var slotIds = list.SelectMany(v => v.SlotIds)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var weekKeys = list.SelectMany(v => v.WeekKeys)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var summary = list.Count == 1
                    ? list[0].Message
                    : $"{list.Count} violation(s) for rule {g.Key.RuleId}.";
                var smallestAffectedSet = new Dictionary<string, object?>
                {
                    ["teams"] = teamIds,
                    ["slots"] = slotIds,
                    ["weeks"] = weekKeys,
                    ["teamCount"] = teamIds.Count,
                    ["slotCount"] = slotIds.Count,
                    ["weekCount"] = weekKeys.Count
                };
                return new ScheduleRuleGroupReport(g.Key.RuleId, g.Key.Severity, list.Count, summary, list, smallestAffectedSet);
            })
            .OrderBy(g => string.Equals(g.Severity, SeverityHard, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(g => g.Count)
            .ThenBy(g => g.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hardCount = violations.Count(v => string.Equals(v.Severity, SeverityHard, StringComparison.OrdinalIgnoreCase));
        var softCount = violations.Count - hardCount;

        var scoreBreakdown = BuildSoftScoreBreakdown(result, teams, config);
        var softPenalty = scoreBreakdown.Sum(t => t.Weighted);
        var softScore = Math.Max(0, 1000 - softPenalty);
        var status = hardCount > 0 ? "red" : (softCount > 0 || softPenalty > 0 ? "yellow" : "green");

        var ruleHealth = new ScheduleRuleHealthReport(
            status,
            ApplyBlocked: hardCount > 0,
            HardViolationCount: hardCount,
            SoftViolationCount: softCount,
            SoftScore: softScore,
            ScoreBreakdown: scoreBreakdown,
            Groups: groups);

        return new ScheduleValidationV2Result(ruleHealth, violations);
    }

    private static void AddBlackoutViolations(
        IEnumerable<ScheduleAssignment> assignments,
        IReadOnlyList<ScheduleBlackoutWindow> blackouts,
        List<ScheduleRuleViolation> violations)
    {
        if (blackouts.Count == 0) return;
        foreach (var a in assignments)
        {
            if (!TryParseDate(a.GameDate, out var date)) continue;
            foreach (var blackout in blackouts)
            {
                if (date < blackout.StartDate || date > blackout.EndDate) continue;
                violations.Add(new ScheduleRuleViolation(
                    "blackout-window",
                    SeverityHard,
                    $"Game assigned during blackout window ({blackout.Label}) on {a.GameDate}.",
                    TeamIds(a.HomeTeamId, a.AwayTeamId),
                    SlotIds(a.SlotId),
                    WeekKeys(a.GameDate),
                    new Dictionary<string, object?>
                    {
                        ["slotId"] = a.SlotId,
                        ["gameDate"] = a.GameDate,
                        ["blackoutRuleId"] = blackout.RuleId,
                        ["blackoutLabel"] = blackout.Label,
                        ["blackoutStart"] = blackout.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["blackoutEnd"] = blackout.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }));
                break;
            }
        }
    }

    private static void AddMissingOpponentViolations(IEnumerable<ScheduleAssignment> assignments, List<ScheduleRuleViolation> violations)
    {
        foreach (var a in assignments)
        {
            if (a.IsExternalOffer) continue;
            if (!string.IsNullOrWhiteSpace(a.HomeTeamId) && !string.IsNullOrWhiteSpace(a.AwayTeamId)) continue;
            violations.Add(new ScheduleRuleViolation(
                "missing-opponent",
                SeverityHard,
                $"Slot {a.SlotId} is missing an opponent assignment.",
                TeamIds(a.HomeTeamId, a.AwayTeamId),
                SlotIds(a.SlotId),
                WeekKeys(a.GameDate),
                new Dictionary<string, object?>
                {
                    ["slotId"] = a.SlotId,
                    ["homeTeamId"] = a.HomeTeamId,
                    ["awayTeamId"] = a.AwayTeamId
                }));
        }
    }

    private static void AddSlotCapacityViolations(IEnumerable<ScheduleAssignment> assignments, List<ScheduleRuleViolation> violations)
    {
        foreach (var group in assignments
            .Where(a => !string.IsNullOrWhiteSpace(a.SlotId))
            .GroupBy(a => a.SlotId, StringComparer.OrdinalIgnoreCase))
        {
            var list = group.ToList();
            if (list.Count <= 1) continue;
            violations.Add(new ScheduleRuleViolation(
                "slot-capacity",
                SeverityHard,
                $"Slot {group.Key} has {list.Count} games assigned.",
                list.SelectMany(a => TeamIds(a.HomeTeamId, a.AwayTeamId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SlotIds(group.Key),
                list.SelectMany(a => WeekKeys(a.GameDate))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                new Dictionary<string, object?> { ["slotId"] = group.Key, ["count"] = list.Count }));
        }
    }

    private static void AddTeamOverlapViolations(IEnumerable<ScheduleAssignment> assignments, List<ScheduleRuleViolation> violations)
    {
        var byTeamDate = new Dictionary<string, Dictionary<string, List<ScheduleAssignment>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments)
        {
            AddTeamDateAssignment(byTeamDate, a.HomeTeamId, a);
            AddTeamDateAssignment(byTeamDate, a.AwayTeamId, a);
        }

        foreach (var (teamId, byDate) in byTeamDate)
        {
            foreach (var (gameDate, list) in byDate)
            {
                var ordered = list
                    .Select(a => new TimeWindow(a, ParseMinutes(a.StartTime), ParseMinutes(a.EndTime)))
                    .Where(x => x.StartMinutes.HasValue && x.EndMinutes.HasValue)
                    .OrderBy(x => x.StartMinutes!.Value)
                    .ThenBy(x => x.EndMinutes!.Value)
                    .ToList();

                for (var i = 0; i < ordered.Count; i++)
                {
                    for (var j = i + 1; j < ordered.Count; j++)
                    {
                        if (ordered[i].EndMinutes!.Value <= ordered[j].StartMinutes!.Value) break;
                        var a1 = ordered[i].Assignment;
                        var a2 = ordered[j].Assignment;
                        violations.Add(new ScheduleRuleViolation(
                            "team-overlap",
                            SeverityHard,
                            $"{teamId} has overlapping games on {gameDate}.",
                            TeamIds(teamId),
                            SlotIds(a1.SlotId, a2.SlotId),
                            WeekKeys(gameDate),
                            new Dictionary<string, object?>
                            {
                                ["teamId"] = teamId,
                                ["gameDate"] = gameDate,
                                ["slotIds"] = new[] { a1.SlotId, a2.SlotId }
                            }));
                    }
                }
            }
        }
    }

    private static void AddDoubleHeaderViolations(IEnumerable<ScheduleAssignment> assignments, bool noDoubleHeaders, List<ScheduleRuleViolation> violations)
    {
        var byTeamDate = BuildTeamDateCounts(assignments);
        foreach (var (teamId, dateMap) in byTeamDate)
        {
            foreach (var (gameDate, count) in dateMap)
            {
                if (count <= 1) continue;
                violations.Add(new ScheduleRuleViolation(
                    "double-header",
                    noDoubleHeaders ? SeverityHard : SeveritySoft,
                    $"{teamId} has {count} games on {gameDate}.",
                    TeamIds(teamId),
                    Array.Empty<string>(),
                    WeekKeys(gameDate),
                    new Dictionary<string, object?>
                    {
                        ["teamId"] = teamId,
                        ["gameDate"] = gameDate,
                        ["count"] = count,
                        ["noDoubleHeaders"] = noDoubleHeaders
                    }));
            }
        }
    }

    private static void AddMaxGamesPerWeekViolations(IEnumerable<ScheduleAssignment> assignments, int? maxGamesPerWeek, List<ScheduleRuleViolation> violations)
    {
        if (!maxGamesPerWeek.HasValue || maxGamesPerWeek.Value <= 0) return;

        var byTeamWeek = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments)
        {
            AddTeamWeekCount(byTeamWeek, a.HomeTeamId, a.GameDate);
            AddTeamWeekCount(byTeamWeek, a.AwayTeamId, a.GameDate);
        }

        foreach (var (teamId, weekMap) in byTeamWeek)
        {
            foreach (var (weekKey, count) in weekMap)
            {
                if (count <= maxGamesPerWeek.Value) continue;
                violations.Add(new ScheduleRuleViolation(
                    "max-games-per-week",
                    SeverityHard,
                    $"{teamId} exceeds {maxGamesPerWeek.Value} games in week {weekKey}.",
                    TeamIds(teamId),
                    Array.Empty<string>(),
                    WeekKeys(weekKey),
                    new Dictionary<string, object?>
                    {
                        ["teamId"] = teamId,
                        ["week"] = weekKey,
                        ["count"] = count,
                        ["limit"] = maxGamesPerWeek.Value
                    }));
            }
        }
    }

    private static void AddUnassignedRequiredMatchupViolations(
        IEnumerable<MatchupPair> unassignedMatchups,
        bool treatAsHard,
        List<ScheduleRuleViolation> violations)
    {
        var list = unassignedMatchups?.ToList() ?? new List<MatchupPair>();
        if (list.Count == 0) return;

        violations.Add(new ScheduleRuleViolation(
            "unscheduled-required-matchups",
            treatAsHard ? SeverityHard : SeveritySoft,
            $"{list.Count} required matchup(s) could not be assigned.",
            list.SelectMany(m => TeamIds(m.HomeTeamId, m.AwayTeamId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, object?>
            {
                ["count"] = list.Count,
                ["sample"] = list.Take(5)
                    .Select(m => new Dictionary<string, object?> { ["homeTeamId"] = m.HomeTeamId, ["awayTeamId"] = m.AwayTeamId })
                    .ToList()
            }));
    }

    private static void AddUnassignedSlotSoftViolations(IEnumerable<ScheduleAssignment> unassignedSlots, List<ScheduleRuleViolation> violations)
    {
        var list = unassignedSlots?.Where(s => !string.IsNullOrWhiteSpace(s.SlotId)).ToList() ?? new List<ScheduleAssignment>();
        if (list.Count == 0) return;
        violations.Add(new ScheduleRuleViolation(
            "unused-game-capacity",
            SeveritySoft,
            $"{list.Count} game-capable slot(s) were left unused.",
            Array.Empty<string>(),
            list.Select(s => s.SlotId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList(),
            list.SelectMany(s => WeekKeys(s.GameDate))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            new Dictionary<string, object?> { ["count"] = list.Count }));
    }

    private static void AddHomeAwayBalanceSoftViolations(
        IEnumerable<ScheduleAssignment> assignments,
        IReadOnlyList<string>? teams,
        bool balanceHomeAway,
        List<ScheduleRuleViolation> violations)
    {
        if (!balanceHomeAway) return;
        var teamList = (teams ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (teamList.Count == 0) return;

        var homeCounts = teamList.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var awayCounts = teamList.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments.Where(a => !a.IsExternalOffer))
        {
            if (homeCounts.ContainsKey(a.HomeTeamId)) homeCounts[a.HomeTeamId] += 1;
            if (awayCounts.ContainsKey(a.AwayTeamId)) awayCounts[a.AwayTeamId] += 1;
        }

        var offenders = teamList
            .Select(t => new TeamGap(t, Math.Abs(homeCounts[t] - awayCounts[t])))
            .Where(x => x.Gap > 1)
            .OrderByDescending(x => x.Gap)
            .ThenBy(x => x.TeamId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (offenders.Count == 0) return;

        violations.Add(new ScheduleRuleViolation(
            "home-away-balance",
            SeveritySoft,
            $"Home/away balance is uneven for {offenders.Count} team(s).",
            offenders.Select(x => x.TeamId).ToList(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, object?>
            {
                ["offenders"] = offenders
                    .Select(x => new Dictionary<string, object?> { ["teamId"] = x.TeamId, ["gap"] = x.Gap })
                    .ToList()
            }));
    }

    private static void AddPairRepeatSoftViolations(
        IEnumerable<ScheduleAssignment> assignments,
        List<ScheduleRuleViolation> violations)
    {
        var games = assignments
            .Where(a => !a.IsExternalOffer && !string.IsNullOrWhiteSpace(a.HomeTeamId) && !string.IsNullOrWhiteSpace(a.AwayTeamId))
            .ToList();
        if (games.Count == 0) return;

        var repeats = games
            .GroupBy(a => PairKey(a.HomeTeamId, a.AwayTeamId), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .Select(g =>
            {
                var list = g.ToList();
                var pairTeams = g.Key.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return new
                {
                    PairKey = g.Key,
                    Count = list.Count,
                    TeamA = pairTeams.Length > 0 ? pairTeams[0] : "",
                    TeamB = pairTeams.Length > 1 ? pairTeams[1] : "",
                    Games = list
                };
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.PairKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (repeats.Count == 0) return;

        violations.Add(new ScheduleRuleViolation(
            "opponent-repeat-balance",
            SeveritySoft,
            $"{repeats.Count} opponent pairing(s) repeat more than once.",
            repeats.SelectMany(x => TeamIds(x.TeamA, x.TeamB))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            repeats.SelectMany(x => x.Games.Select(g => g.SlotId))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList(),
            repeats.SelectMany(x => x.Games.SelectMany(g => WeekKeys(g.GameDate)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            new Dictionary<string, object?>
            {
                ["pairs"] = repeats.Take(10)
                    .Select(x => new Dictionary<string, object?>
                    {
                        ["pairKey"] = x.PairKey,
                        ["count"] = x.Count
                    })
                    .ToList()
            }));
    }

    private static void AddIdleGapSoftViolations(
        IEnumerable<ScheduleAssignment> assignments,
        IReadOnlyList<string>? teams,
        List<ScheduleRuleViolation> violations)
    {
        var teamList = (teams ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (teamList.Count == 0) return;

        var byTeamDates = BuildTeamGameDates(assignments);
        var offenders = teamList
            .Select(teamId =>
            {
                var dates = byTeamDates.TryGetValue(teamId, out var list) ? list : new List<DateOnly>();
                dates.Sort();
                var extraGapWeeks = 0;
                var gapSegments = new List<Dictionary<string, object?>>();
                for (var i = 1; i < dates.Count; i++)
                {
                    var gapDays = dates[i].DayNumber - dates[i - 1].DayNumber;
                    var extraWeeks = Math.Max(0, (gapDays - 7) / 7);
                    if (extraWeeks <= 0) continue;
                    extraGapWeeks += extraWeeks;
                    gapSegments.Add(new Dictionary<string, object?>
                    {
                        ["startDate"] = dates[i - 1].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["endDate"] = dates[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["gapDays"] = gapDays,
                        ["extraGapWeeks"] = extraWeeks
                    });
                }
                return new { TeamId = teamId, ExtraGapWeeks = extraGapWeeks, Segments = gapSegments };
            })
            .Where(x => x.ExtraGapWeeks > 0)
            .OrderByDescending(x => x.ExtraGapWeeks)
            .ThenBy(x => x.TeamId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offenders.Count == 0) return;

        violations.Add(new ScheduleRuleViolation(
            "idle-gap-balance",
            SeveritySoft,
            $"Long idle gaps detected for {offenders.Count} team(s).",
            offenders.Select(x => x.TeamId).ToList(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, object?>
            {
                ["offenders"] = offenders.Take(10)
                    .Select(x => new Dictionary<string, object?>
                    {
                        ["teamId"] = x.TeamId,
                        ["extraGapWeeks"] = x.ExtraGapWeeks,
                        ["segments"] = x.Segments
                    })
                    .ToList()
            }));
    }

    private static List<ScheduleSoftScoreTerm> BuildSoftScoreBreakdown(
        ScheduleResult result,
        IReadOnlyList<string>? teams,
        ScheduleValidationV2Config config)
    {
        var terms = new List<ScheduleSoftScoreTerm>();

        var unusedSlots = result.UnassignedSlots.Count;
        terms.Add(new ScheduleSoftScoreTerm("unused-game-capacity", 5, unusedSlots, unusedSlots * 5));

        var weatherWeightedUnused = ComputeWeatherWeightedUnusedCapacityPenalty(
            result.Assignments,
            result.UnassignedSlots,
            config.PhaseReliabilityWeights ?? SchedulePhaseReliabilityWeights.Default);
        terms.Add(new ScheduleSoftScoreTerm("weather-weighted-unused-capacity", 2, weatherWeightedUnused, weatherWeightedUnused * 2));

        var pairRepeatRaw = result.Assignments
            .Where(a => !a.IsExternalOffer && !string.IsNullOrWhiteSpace(a.HomeTeamId) && !string.IsNullOrWhiteSpace(a.AwayTeamId))
            .GroupBy(a => PairKey(a.HomeTeamId, a.AwayTeamId), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Sum(g => Math.Max(0, g.Count() - 1));
        terms.Add(new ScheduleSoftScoreTerm("opponent-repeat-overage", 4, pairRepeatRaw, pairRepeatRaw * 4));

        var idleGapRaw = ComputeIdleGapExtraWeeks(result.Assignments, teams);
        terms.Add(new ScheduleSoftScoreTerm("idle-gap-extra-weeks", 3, idleGapRaw, idleGapRaw * 3));

        var latePriorityRaw = ComputeLatePriorityPlacementPenalty(result, config.MatchupPriorityByPair);
        terms.Add(new ScheduleSoftScoreTerm("late-priority-placement", 2, latePriorityRaw, latePriorityRaw * 2));

        if (!config.BalanceHomeAway || teams is null || teams.Count == 0)
        {
            terms.Add(new ScheduleSoftScoreTerm("home-away-balance-gap", 0, 0, 0));
            return terms;
        }

        var teamList = teams.Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var homeCounts = teamList.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var awayCounts = teamList.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var a in result.Assignments.Where(a => !a.IsExternalOffer))
        {
            if (homeCounts.ContainsKey(a.HomeTeamId)) homeCounts[a.HomeTeamId] += 1;
            if (awayCounts.ContainsKey(a.AwayTeamId)) awayCounts[a.AwayTeamId] += 1;
        }
        var homeAwayGapSum = teamList.Sum(t => Math.Abs(homeCounts[t] - awayCounts[t]));
        terms.Add(new ScheduleSoftScoreTerm("home-away-balance-gap", 2, homeAwayGapSum, homeAwayGapSum * 2));
        return terms;
    }

    private static double ComputeIdleGapExtraWeeks(IEnumerable<ScheduleAssignment> assignments, IReadOnlyList<string>? teams)
    {
        var teamList = (teams ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (teamList.Count == 0) return 0;

        var byTeamDates = BuildTeamGameDates(assignments);
        var total = 0;
        foreach (var teamId in teamList)
        {
            if (!byTeamDates.TryGetValue(teamId, out var dates) || dates.Count < 2) continue;
            dates.Sort();
            for (var i = 1; i < dates.Count; i++)
            {
                var gapDays = dates[i].DayNumber - dates[i - 1].DayNumber;
                total += Math.Max(0, (gapDays - 7) / 7);
            }
        }

        return total;
    }

    private static double ComputeLatePriorityPlacementPenalty(
        ScheduleResult result,
        IReadOnlyDictionary<string, int>? matchupPriorityByPair)
    {
        if (matchupPriorityByPair is null || matchupPriorityByPair.Count == 0) return 0;

        var allDates = result.Assignments
            .Concat(result.UnassignedSlots)
            .Select(a => TryParseDate(a.GameDate, out var date) ? date : (DateOnly?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        if (allDates.Count < 2) return 0;

        var minDate = allDates[0];
        var maxDate = allDates[^1];
        var totalDays = Math.Max(0, maxDate.DayNumber - minDate.DayNumber);
        if (totalDays <= 0) return 0;

        double totalPenalty = 0;
        foreach (var a in result.Assignments.Where(a => !a.IsExternalOffer))
        {
            var pairKey = PairKey(a.HomeTeamId, a.AwayTeamId);
            if (string.IsNullOrWhiteSpace(pairKey)) continue;
            if (!matchupPriorityByPair.TryGetValue(pairKey, out var weight) || weight <= 0) continue;
            if (!TryParseDate(a.GameDate, out var date)) continue;

            var daysFromStart = Math.Clamp(date.DayNumber - minDate.DayNumber, 0, totalDays);
            var earlinessDays = totalDays - daysFromStart;
            totalPenalty += ((double)weight * earlinessDays) / totalDays;
        }

        return totalPenalty;
    }

    private static double ComputeWeatherWeightedUnusedCapacityPenalty(
        IReadOnlyList<ScheduleAssignment> assignments,
        IReadOnlyList<ScheduleAssignment> unassignedSlots,
        SchedulePhaseReliabilityWeights weights)
    {
        if (unassignedSlots is null || unassignedSlots.Count == 0) return 0;
        var allDates = assignments
            .Concat(unassignedSlots)
            .Select(a => TryParseDate(a.GameDate, out var d) ? d : (DateOnly?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderBy(d => d)
            .ToList();
        if (allDates.Count == 0) return 0;

        var minDate = allDates[0];
        var maxDate = allDates[^1];
        var spanDays = Math.Max(0, maxDate.DayNumber - minDate.DayNumber);
        if (spanDays <= 0)
            return unassignedSlots.Count * Math.Max(0, weights.Late);

        double total = 0;
        foreach (var slot in unassignedSlots)
        {
            if (!TryParseDate(slot.GameDate, out var date)) continue;
            var position = (double)(date.DayNumber - minDate.DayNumber) / spanDays;
            total += position switch
            {
                < (1.0 / 3.0) => Math.Max(0, weights.Early),
                < (2.0 / 3.0) => Math.Max(0, weights.Mid),
                _ => Math.Max(0, weights.Late)
            };
        }

        return Math.Round(total, 3);
    }

    private static void AddNoGamesOnDatesViolations(
        IEnumerable<ScheduleAssignment> assignments,
        IReadOnlyCollection<string>? noGamesOnDates,
        List<ScheduleRuleViolation> violations)
    {
        if (noGamesOnDates is null || noGamesOnDates.Count == 0) return;
        var blocked = new HashSet<string>(
            noGamesOnDates.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (blocked.Count == 0) return;

        foreach (var a in assignments)
        {
            if (!blocked.Contains(a.GameDate)) continue;
            violations.Add(new ScheduleRuleViolation(
                "no-games-on-date",
                SeverityHard,
                $"Game assigned on blocked date {a.GameDate}.",
                TeamIds(a.HomeTeamId, a.AwayTeamId),
                SlotIds(a.SlotId),
                WeekKeys(a.GameDate),
                new Dictionary<string, object?>
                {
                    ["slotId"] = a.SlotId,
                    ["gameDate"] = a.GameDate
                }));
        }
    }

    private static void AddTimeWindowViolations(
        IEnumerable<ScheduleAssignment> assignments,
        int? noGamesBeforeMinute,
        int? noGamesAfterMinute,
        List<ScheduleRuleViolation> violations)
    {
        if (!noGamesBeforeMinute.HasValue && !noGamesAfterMinute.HasValue) return;

        foreach (var a in assignments)
        {
            var start = ParseMinutes(a.StartTime);
            var end = ParseMinutes(a.EndTime);
            if (!start.HasValue && !end.HasValue) continue;

            if (noGamesBeforeMinute.HasValue && start.HasValue && start.Value < noGamesBeforeMinute.Value)
            {
                violations.Add(new ScheduleRuleViolation(
                    "no-games-before-time",
                    SeverityHard,
                    $"Game starts before allowed time on {a.GameDate} ({a.StartTime}).",
                    TeamIds(a.HomeTeamId, a.AwayTeamId),
                    SlotIds(a.SlotId),
                    WeekKeys(a.GameDate),
                    new Dictionary<string, object?>
                    {
                        ["slotId"] = a.SlotId,
                        ["gameDate"] = a.GameDate,
                        ["startTime"] = a.StartTime,
                        ["limitMinute"] = noGamesBeforeMinute.Value
                    }));
            }

            if (noGamesAfterMinute.HasValue && end.HasValue && end.Value > noGamesAfterMinute.Value)
            {
                violations.Add(new ScheduleRuleViolation(
                    "no-games-after-time",
                    SeverityHard,
                    $"Game ends after allowed time on {a.GameDate} ({a.EndTime}).",
                    TeamIds(a.HomeTeamId, a.AwayTeamId),
                    SlotIds(a.SlotId),
                    WeekKeys(a.GameDate),
                    new Dictionary<string, object?>
                    {
                        ["slotId"] = a.SlotId,
                        ["gameDate"] = a.GameDate,
                        ["endTime"] = a.EndTime,
                        ["limitMinute"] = noGamesAfterMinute.Value
                    }));
            }
        }
    }

    private static void AddExternalOfferCapViolations(
        IEnumerable<ScheduleAssignment> assignments,
        int? maxExternalOffersPerTeamSeason,
        List<ScheduleRuleViolation> violations)
    {
        if (!maxExternalOffersPerTeamSeason.HasValue || maxExternalOffersPerTeamSeason.Value <= 0) return;

        var counts = assignments
            .Where(a => a.IsExternalOffer && !string.IsNullOrWhiteSpace(a.HomeTeamId))
            .GroupBy(a => a.HomeTeamId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                TeamId = g.Key,
                Count = g.Count(),
                Slots = g.Select(a => a.SlotId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Weeks = g.SelectMany(a => WeekKeys(a.GameDate)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            })
            .Where(x => x.Count > maxExternalOffersPerTeamSeason.Value)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.TeamId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var offender in counts)
        {
            violations.Add(new ScheduleRuleViolation(
                "max-external-offers-per-team",
                SeverityHard,
                $"{offender.TeamId} exceeds external/crossover offer limit ({offender.Count} > {maxExternalOffersPerTeamSeason.Value}).",
                TeamIds(offender.TeamId),
                offender.Slots,
                offender.Weeks,
                new Dictionary<string, object?>
                {
                    ["teamId"] = offender.TeamId,
                    ["count"] = offender.Count,
                    ["limit"] = maxExternalOffersPerTeamSeason.Value
                }));
        }
    }

    private static void AddTeamDateAssignment(
        Dictionary<string, Dictionary<string, List<ScheduleAssignment>>> byTeamDate,
        string teamId,
        ScheduleAssignment assignment)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return;
        if (!byTeamDate.TryGetValue(teamId, out var byDate))
        {
            byDate = new Dictionary<string, List<ScheduleAssignment>>(StringComparer.OrdinalIgnoreCase);
            byTeamDate[teamId] = byDate;
        }
        if (!byDate.TryGetValue(assignment.GameDate, out var list))
        {
            list = new List<ScheduleAssignment>();
            byDate[assignment.GameDate] = list;
        }
        list.Add(assignment);
    }

    private static Dictionary<string, Dictionary<string, int>> BuildTeamDateCounts(IEnumerable<ScheduleAssignment> assignments)
    {
        var byTeamDate = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments)
        {
            AddTeamDateCount(byTeamDate, a.HomeTeamId, a.GameDate);
            AddTeamDateCount(byTeamDate, a.AwayTeamId, a.GameDate);
        }
        return byTeamDate;
    }

    private static void AddTeamDateCount(Dictionary<string, Dictionary<string, int>> byTeamDate, string teamId, string gameDate)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return;
        if (!byTeamDate.TryGetValue(teamId, out var dates))
        {
            dates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            byTeamDate[teamId] = dates;
        }
        dates[gameDate] = dates.TryGetValue(gameDate, out var count) ? count + 1 : 1;
    }

    private static Dictionary<string, List<DateOnly>> BuildTeamGameDates(IEnumerable<ScheduleAssignment> assignments)
    {
        var byTeamDates = new Dictionary<string, List<DateOnly>>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments.Where(a => !a.IsExternalOffer))
        {
            if (!TryParseDate(a.GameDate, out var date)) continue;
            AddTeamGameDate(byTeamDates, a.HomeTeamId, date);
            AddTeamGameDate(byTeamDates, a.AwayTeamId, date);
        }

        return byTeamDates;
    }

    private static void AddTeamGameDate(Dictionary<string, List<DateOnly>> byTeamDates, string teamId, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return;
        if (!byTeamDates.TryGetValue(teamId, out var dates))
        {
            dates = new List<DateOnly>();
            byTeamDates[teamId] = dates;
        }
        dates.Add(date);
    }

    private static void AddTeamWeekCount(Dictionary<string, Dictionary<string, int>> byTeamWeek, string teamId, string gameDate)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return;
        var week = WeekKeyFromDate(gameDate);
        if (string.IsNullOrWhiteSpace(week)) return;
        if (!byTeamWeek.TryGetValue(teamId, out var weeks))
        {
            weeks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            byTeamWeek[teamId] = weeks;
        }
        weeks[week] = weeks.TryGetValue(week, out var count) ? count + 1 : 1;
    }

    private static string WeekKeyFromDate(string gameDate)
    {
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return "";
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{dt.Year}-W{week:D2}";
    }

    private static bool TryParseDate(string gameDate, out DateOnly date)
        => DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static int? ParseMinutes(string hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        if (!TimeOnly.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return null;
        return (t.Hour * 60) + t.Minute;
    }

    private static IReadOnlyList<string> TeamIds(params string[] teamIds)
        => teamIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> SlotIds(params string[] slotIds)
        => slotIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> WeekKeys(params string[] gameDatesOrWeekKeys)
    {
        var keys = new List<string>();
        foreach (var raw in gameDatesOrWeekKeys)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var key = raw.Contains("-W", StringComparison.OrdinalIgnoreCase) ? raw : WeekKeyFromDate(raw);
            if (string.IsNullOrWhiteSpace(key)) continue;
            keys.Add(key);
        }
        return keys.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private readonly record struct RuleGroupKey(string RuleId, string Severity);
    private readonly record struct TimeWindow(ScheduleAssignment Assignment, int? StartMinutes, int? EndMinutes);
    private readonly record struct TeamGap(string TeamId, int Gap);

    private static string PairKey(string homeTeamId, string awayTeamId)
    {
        if (string.IsNullOrWhiteSpace(homeTeamId) || string.IsNullOrWhiteSpace(awayTeamId)) return "";
        return string.Compare(homeTeamId, awayTeamId, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{homeTeamId}|{awayTeamId}"
            : $"{awayTeamId}|{homeTeamId}";
    }
}
