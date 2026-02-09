using System.Globalization;

namespace GameSwap.Functions.Scheduling;

public record ValidationIssue(string RuleId, string Severity, string Message, Dictionary<string, object?> Details);

public record ScheduleValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public int TotalIssues => Issues.Count;
}

public static class ScheduleValidation
{
    public static ScheduleValidationResult Validate(ScheduleResult result, ScheduleConstraints constraints)
    {
        var issues = new List<ValidationIssue>();

        AddMissingOpponentIssues(result.Assignments, issues);
        AddDoubleHeaderIssues(result.Assignments, issues);
        AddDoubleHeaderBalanceIssues(result.Assignments, constraints.NoDoubleHeaders, issues);
        AddMaxGamesPerWeekIssues(result.Assignments, constraints.MaxGamesPerWeek, issues);

        if (result.UnassignedMatchups.Count > 0)
        {
            issues.Add(new ValidationIssue(
                "unassigned-matchups",
                "warning",
                $"{result.UnassignedMatchups.Count} matchup(s) were not assigned.",
                new Dictionary<string, object?> { ["count"] = result.UnassignedMatchups.Count }));
        }

        if (result.UnassignedSlots.Count > 0)
        {
            issues.Add(new ValidationIssue(
                "unassigned-slots",
                "warning",
                $"{result.UnassignedSlots.Count} slot(s) were left unassigned.",
                new Dictionary<string, object?> { ["count"] = result.UnassignedSlots.Count }));
        }

        return new ScheduleValidationResult(issues);
    }

    private static void AddMissingOpponentIssues(IEnumerable<ScheduleAssignment> assignments, List<ValidationIssue> issues)
    {
        foreach (var a in assignments)
        {
            if (a.IsExternalOffer) continue;
            if (string.IsNullOrWhiteSpace(a.HomeTeamId) || string.IsNullOrWhiteSpace(a.AwayTeamId))
            {
                issues.Add(new ValidationIssue(
                    "missing-opponent",
                    "warning",
                    $"Slot {a.SlotId} is missing an opponent assignment.",
                    new Dictionary<string, object?>
                    {
                        ["slotId"] = a.SlotId,
                        ["homeTeamId"] = a.HomeTeamId,
                        ["awayTeamId"] = a.AwayTeamId
                    }));
            }
        }
    }

    private static void AddDoubleHeaderIssues(IEnumerable<ScheduleAssignment> assignments, List<ValidationIssue> issues)
    {
        var byTeamDate = BuildTeamDateCounts(assignments);

        foreach (var (teamId, dateMap) in byTeamDate)
        {
            foreach (var (date, count) in dateMap)
            {
                if (count <= 1) continue;
                issues.Add(new ValidationIssue(
                    "double-header",
                    "warning",
                    $"{teamId} has {count} games on {date}.",
                    new Dictionary<string, object?> { ["teamId"] = teamId, ["gameDate"] = date, ["count"] = count }));
            }
        }
    }

    private static void AddDoubleHeaderBalanceIssues(IEnumerable<ScheduleAssignment> assignments, bool noDoubleHeaders, List<ValidationIssue> issues)
    {
        if (noDoubleHeaders) return;

        var byTeamDate = BuildTeamDateCounts(assignments);
        if (byTeamDate.Count < 2) return;

        var doubleHeaderCounts = byTeamDate.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Values.Where(count => count > 1).Sum(count => count - 1),
            StringComparer.OrdinalIgnoreCase);

        if (doubleHeaderCounts.Count < 2) return;

        var maxDoubleHeaders = doubleHeaderCounts.Values.Max();
        var minDoubleHeaders = doubleHeaderCounts.Values.Min();
        var gap = maxDoubleHeaders - minDoubleHeaders;
        var allowedGap = byTeamDate.Count % 2 == 1 ? 1 : 0;
        if (gap <= allowedGap) return;

        var maxTeams = doubleHeaderCounts
            .Where(kvp => kvp.Value == maxDoubleHeaders)
            .Select(kvp => kvp.Key)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var minTeams = doubleHeaderCounts
            .Where(kvp => kvp.Value == minDoubleHeaders)
            .Select(kvp => kvp.Key)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        issues.Add(new ValidationIssue(
            "double-header-balance",
            "warning",
            $"Doubleheader distribution is uneven (max {maxDoubleHeaders}, min {minDoubleHeaders}).",
            new Dictionary<string, object?>
            {
                ["maxDoubleHeaders"] = maxDoubleHeaders,
                ["minDoubleHeaders"] = minDoubleHeaders,
                ["gap"] = gap,
                ["allowedGap"] = allowedGap,
                ["teamCount"] = byTeamDate.Count,
                ["teamsAtMax"] = maxTeams,
                ["teamsAtMin"] = minTeams
            }));
    }

    private static void AddMaxGamesPerWeekIssues(IEnumerable<ScheduleAssignment> assignments, int? maxGamesPerWeek, List<ValidationIssue> issues)
    {
        if (!maxGamesPerWeek.HasValue) return;

        var byTeamWeek = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments)
        {
            AddTeamWeekCount(byTeamWeek, a.HomeTeamId, a.GameDate);
            AddTeamWeekCount(byTeamWeek, a.AwayTeamId, a.GameDate);
        }

        foreach (var (teamId, weekMap) in byTeamWeek)
        {
            foreach (var (week, count) in weekMap)
            {
                if (count <= maxGamesPerWeek.Value) continue;
                issues.Add(new ValidationIssue(
                    "max-games-per-week",
                    "warning",
                    $"{teamId} exceeds {maxGamesPerWeek} games in week {week}.",
                    new Dictionary<string, object?> { ["teamId"] = teamId, ["week"] = week, ["count"] = count, ["limit"] = maxGamesPerWeek.Value }));
            }
        }
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

    private static void AddTeamWeekCount(Dictionary<string, Dictionary<string, int>> byTeamWeek, string teamId, string gameDate)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return;
        if (!byTeamWeek.TryGetValue(teamId, out var weeks))
        {
            weeks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            byTeamWeek[teamId] = weeks;
        }

        var week = WeekKey(gameDate);
        if (string.IsNullOrWhiteSpace(week)) return;
        weeks[week] = weeks.TryGetValue(week, out var count) ? count + 1 : 1;
    }

    private static string WeekKey(string gameDate)
    {
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return "";
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{dt.Year}-W{week:D2}";
    }
}
