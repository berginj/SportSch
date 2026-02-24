using System.Globalization;

namespace GameSwap.Functions.Scheduling;

public record ScheduleValidationV2Config(
    int? MaxGamesPerWeek,
    bool NoDoubleHeaders,
    bool BalanceHomeAway,
    IReadOnlyList<ScheduleBlackoutWindow>? BlackoutWindows = null,
    bool TreatUnassignedRequiredMatchupsAsHard = true);

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
        AddUnassignedRequiredMatchupViolations(result.UnassignedMatchups, config.TreatUnassignedRequiredMatchupsAsHard, violations);
        AddUnassignedSlotSoftViolations(result.UnassignedSlots, violations);
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

    private static List<ScheduleSoftScoreTerm> BuildSoftScoreBreakdown(
        ScheduleResult result,
        IReadOnlyList<string>? teams,
        ScheduleValidationV2Config config)
    {
        var terms = new List<ScheduleSoftScoreTerm>();

        var unusedSlots = result.UnassignedSlots.Count;
        terms.Add(new ScheduleSoftScoreTerm("unused-game-capacity", 5, unusedSlots, unusedSlots * 5));

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
}
