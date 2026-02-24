using System.Globalization;

namespace GameSwap.Functions.Scheduling;

public record ScheduleConstraints(int? MaxGamesPerWeek, bool NoDoubleHeaders, bool BalanceHomeAway, int ExternalOfferPerWeek);

public record ScheduleSlot(
    string SlotId,
    string GameDate,
    string StartTime,
    string EndTime,
    string FieldKey,
    string OfferingTeamId);

public record ScheduleAssignment(
    string SlotId,
    string GameDate,
    string StartTime,
    string EndTime,
    string FieldKey,
    string HomeTeamId,
    string AwayTeamId,
    bool IsExternalOffer);

public record MatchupPair(string HomeTeamId, string AwayTeamId);

public record ScheduleSummary(
    int SlotsTotal,
    int SlotsAssigned,
    int MatchupsTotal,
    int MatchupsAssigned,
    int ExternalOffers,
    int UnassignedSlots,
    int UnassignedMatchups);

public record ScheduleResult(
    ScheduleSummary Summary,
    List<ScheduleAssignment> Assignments,
    List<ScheduleAssignment> UnassignedSlots,
    List<MatchupPair> UnassignedMatchups,
    List<SchedulePlacementTrace>? PlacementTraces = null);

public record ScheduleScoreBreakdown(
    int TeamVolumePenalty,
    int TeamImbalancePenalty,
    int TeamLoadSpreadPenalty,
    int HomeAwayPenalty,
    int TotalScore);

public record ScheduleCandidateTrace(
    string HomeTeamId,
    string AwayTeamId,
    bool Feasible,
    string? RejectReason,
    ScheduleScoreBreakdown? ScoreBreakdown);

public record SchedulePlacementTrace(
    string SlotId,
    string GameDate,
    string StartTime,
    string EndTime,
    string FieldKey,
    int SlotOrderIndex,
    string Outcome,
    string? FixedHomeTeamId,
    string? SelectedHomeTeamId,
    string? SelectedAwayTeamId,
    ScheduleScoreBreakdown? SelectedScoreBreakdown,
    int CandidateCount,
    int FeasibleCandidateCount,
    List<ScheduleCandidateTrace> TopFeasibleAlternatives,
    List<ScheduleCandidateTrace> TopRejectedAlternatives);

public static class ScheduleEngine
{
    private sealed class MatchupPickTraceResult
    {
        public MatchupPair? Pick { get; init; }
        public ScheduleScoreBreakdown? SelectedScoreBreakdown { get; init; }
        public List<ScheduleCandidateTrace> TopFeasibleAlternatives { get; init; } = new();
        public List<ScheduleCandidateTrace> TopRejectedAlternatives { get; init; } = new();
        public int CandidateCount { get; init; }
        public int FeasibleCandidateCount { get; init; }
    }

    public static List<MatchupPair> BuildRoundRobin(IReadOnlyList<string> teamIds)
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

    public static ScheduleResult AssignMatchups(
        IReadOnlyList<ScheduleSlot> slots,
        IReadOnlyList<MatchupPair> matchups,
        IReadOnlyList<string> teams,
        ScheduleConstraints constraints,
        bool includePlacementTraces = false)
    {
        var teamSet = new HashSet<string>(teams, StringComparer.OrdinalIgnoreCase);
        var homeCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var awayCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var gamesByDate = teams.ToDictionary(t => t, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var gamesByWeek = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var assignments = new List<ScheduleAssignment>();
        var remainingMatchups = new List<MatchupPair>(matchups);
        var unassignedSlots = new List<ScheduleAssignment>();
        var placementTraces = includePlacementTraces ? new List<SchedulePlacementTrace>() : null;

        var slotOrderIndex = 0;
        foreach (var slot in slots)
        {
            var fixedHome = teamSet.Contains(slot.OfferingTeamId) ? slot.OfferingTeamId : "";
            MatchupPair? pick;
            ScheduleScoreBreakdown? selectedScoreBreakdown = null;
            List<ScheduleCandidateTrace> topFeasibleAlternatives = new();
            List<ScheduleCandidateTrace> topRejectedAlternatives = new();
            var candidateCount = 0;
            var feasibleCandidateCount = 0;

            if (includePlacementTraces)
            {
                var tracedPick = PickMatchupWithTrace(
                    slot.GameDate,
                    fixedHome,
                    remainingMatchups,
                    teams,
                    homeCounts,
                    awayCounts,
                    gamesByDate,
                    gamesByWeek,
                    constraints.MaxGamesPerWeek,
                    constraints.NoDoubleHeaders,
                    constraints.BalanceHomeAway);
                pick = tracedPick.Pick;
                selectedScoreBreakdown = tracedPick.SelectedScoreBreakdown;
                topFeasibleAlternatives = tracedPick.TopFeasibleAlternatives;
                topRejectedAlternatives = tracedPick.TopRejectedAlternatives;
                candidateCount = tracedPick.CandidateCount;
                feasibleCandidateCount = tracedPick.FeasibleCandidateCount;
            }
            else
            {
                pick = PickMatchup(slot.GameDate, fixedHome, remainingMatchups, teams, homeCounts, awayCounts, gamesByDate, gamesByWeek, constraints.MaxGamesPerWeek, constraints.NoDoubleHeaders, constraints.BalanceHomeAway);
            }

            if (pick is null)
            {
                unassignedSlots.Add(new ScheduleAssignment(slot.SlotId, slot.GameDate, slot.StartTime, slot.EndTime, slot.FieldKey, "", "", false));
                if (placementTraces is not null)
                {
                    placementTraces.Add(new SchedulePlacementTrace(
                        SlotId: slot.SlotId,
                        GameDate: slot.GameDate,
                        StartTime: slot.StartTime,
                        EndTime: slot.EndTime,
                        FieldKey: slot.FieldKey,
                        SlotOrderIndex: slotOrderIndex,
                        Outcome: "open",
                        FixedHomeTeamId: string.IsNullOrWhiteSpace(fixedHome) ? null : fixedHome,
                        SelectedHomeTeamId: null,
                        SelectedAwayTeamId: null,
                        SelectedScoreBreakdown: null,
                        CandidateCount: candidateCount,
                        FeasibleCandidateCount: feasibleCandidateCount,
                        TopFeasibleAlternatives: topFeasibleAlternatives,
                        TopRejectedAlternatives: topRejectedAlternatives));
                }
                slotOrderIndex += 1;
                continue;
            }

            var home = pick.HomeTeamId;
            var away = pick.AwayTeamId;
            remainingMatchups.Remove(pick);
            ApplyCounts(home, away, slot.GameDate, homeCounts, awayCounts, gamesByDate, gamesByWeek);
            assignments.Add(new ScheduleAssignment(slot.SlotId, slot.GameDate, slot.StartTime, slot.EndTime, slot.FieldKey, home, away, false));
            if (placementTraces is not null)
            {
                placementTraces.Add(new SchedulePlacementTrace(
                    SlotId: slot.SlotId,
                    GameDate: slot.GameDate,
                    StartTime: slot.StartTime,
                    EndTime: slot.EndTime,
                    FieldKey: slot.FieldKey,
                    SlotOrderIndex: slotOrderIndex,
                    Outcome: "assigned",
                    FixedHomeTeamId: string.IsNullOrWhiteSpace(fixedHome) ? null : fixedHome,
                    SelectedHomeTeamId: home,
                    SelectedAwayTeamId: away,
                    SelectedScoreBreakdown: selectedScoreBreakdown,
                    CandidateCount: candidateCount,
                    FeasibleCandidateCount: feasibleCandidateCount,
                    TopFeasibleAlternatives: topFeasibleAlternatives,
                    TopRejectedAlternatives: topRejectedAlternatives));
            }
            slotOrderIndex += 1;
        }

        if (constraints.ExternalOfferPerWeek > 0 && unassignedSlots.Count > 0)
        {
            var remaining = new List<ScheduleAssignment>();
            var byWeek = unassignedSlots
                .GroupBy(s => WeekKey(s.GameDate))
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in byWeek)
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    remaining.AddRange(group);
                    continue;
                }

                var picks = group.Take(constraints.ExternalOfferPerWeek).ToList();
                foreach (var slot in picks)
                {
                    var home = PickExternalHome(teams, homeCounts, awayCounts);
                    ApplyCounts(home, "", slot.GameDate, homeCounts, awayCounts, gamesByDate, gamesByWeek);
                    assignments.Add(new ScheduleAssignment(slot.SlotId, slot.GameDate, slot.StartTime, slot.EndTime, slot.FieldKey, home, "", true));
                }

                remaining.AddRange(group.Skip(constraints.ExternalOfferPerWeek));
            }

            unassignedSlots = remaining;
        }

        var unassignedMatchups = remainingMatchups;

        var summary = new ScheduleSummary(
            SlotsTotal: slots.Count,
            SlotsAssigned: assignments.Count,
            MatchupsTotal: matchups.Count,
            MatchupsAssigned: matchups.Count - remainingMatchups.Count,
            ExternalOffers: assignments.Count(a => a.IsExternalOffer),
            UnassignedSlots: unassignedSlots.Count,
            UnassignedMatchups: remainingMatchups.Count);

        return new ScheduleResult(summary, assignments, unassignedSlots, unassignedMatchups, placementTraces);
    }

    private static MatchupPair? PickMatchup(
        string gameDate,
        string fixedHome,
        List<MatchupPair> matchups,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway)
    {
        MatchupPair? best = null;
        var bestScore = int.MaxValue;

        foreach (var m in matchups)
        {
            var home = m.HomeTeamId;
            var away = m.AwayTeamId;

            if (!string.IsNullOrWhiteSpace(fixedHome))
            {
                if (!string.Equals(home, fixedHome, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(away, fixedHome, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(home, fixedHome, StringComparison.OrdinalIgnoreCase))
                {
                    home = fixedHome;
                    away = m.HomeTeamId;
                }
            }

            if (!CanAssign(home, away, gameDate, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders)) continue;

            var score = ScoreCandidate(home, away, teams, homeCounts, awayCounts, balanceHomeAway);

            if (score < bestScore)
            {
                bestScore = score;
                best = new MatchupPair(home, away);
                if (score == 0) break;
            }
        }

        return best;
    }

    private static MatchupPickTraceResult PickMatchupWithTrace(
        string gameDate,
        string fixedHome,
        List<MatchupPair> matchups,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway)
    {
        MatchupPair? best = null;
        ScheduleScoreBreakdown? bestBreakdown = null;
        var bestScore = int.MaxValue;
        var candidates = new List<ScheduleCandidateTrace>(matchups.Count);
        var feasibleCount = 0;

        foreach (var m in matchups)
        {
            var home = m.HomeTeamId;
            var away = m.AwayTeamId;

            if (!string.IsNullOrWhiteSpace(fixedHome))
            {
                if (!string.Equals(home, fixedHome, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(away, fixedHome, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(new ScheduleCandidateTrace(
                        HomeTeamId: home,
                        AwayTeamId: away,
                        Feasible: false,
                        RejectReason: $"does-not-include-fixed-home:{fixedHome}",
                        ScoreBreakdown: null));
                    continue;
                }

                if (!string.Equals(home, fixedHome, StringComparison.OrdinalIgnoreCase))
                {
                    home = fixedHome;
                    away = m.HomeTeamId;
                }
            }

            var rejectReason = GetConstraintRejectReason(home, away, gameDate, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders);
            if (!string.IsNullOrWhiteSpace(rejectReason))
            {
                candidates.Add(new ScheduleCandidateTrace(
                    HomeTeamId: home,
                    AwayTeamId: away,
                    Feasible: false,
                    RejectReason: rejectReason,
                    ScoreBreakdown: null));
                continue;
            }

            feasibleCount += 1;
            var breakdown = ScoreCandidateBreakdown(home, away, teams, homeCounts, awayCounts, balanceHomeAway);
            candidates.Add(new ScheduleCandidateTrace(
                HomeTeamId: home,
                AwayTeamId: away,
                Feasible: true,
                RejectReason: null,
                ScoreBreakdown: breakdown));

            if (breakdown.TotalScore < bestScore)
            {
                bestScore = breakdown.TotalScore;
                bestBreakdown = breakdown;
                best = new MatchupPair(home, away);
                if (bestScore == 0) break;
            }
        }

        var topFeasible = candidates
            .Where(c => c.Feasible && c.ScoreBreakdown is not null)
            .OrderBy(c => c.ScoreBreakdown!.TotalScore)
            .ThenBy(c => c.HomeTeamId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.AwayTeamId, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        var topRejected = candidates
            .Where(c => !c.Feasible)
            .OrderBy(c => c.RejectReason, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.HomeTeamId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.AwayTeamId, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return new MatchupPickTraceResult
        {
            Pick = best,
            SelectedScoreBreakdown = bestBreakdown,
            TopFeasibleAlternatives = topFeasible,
            TopRejectedAlternatives = topRejected,
            CandidateCount = candidates.Count,
            FeasibleCandidateCount = feasibleCount
        };
    }

    private static int ScoreCandidate(
        string home,
        string away,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        bool balanceHomeAway)
    {
        return ScoreCandidateBreakdown(home, away, teams, homeCounts, awayCounts, balanceHomeAway).TotalScore;
    }

    private static ScheduleScoreBreakdown ScoreCandidateBreakdown(
        string home,
        string away,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        bool balanceHomeAway)
    {
        var homeGames = homeCounts[home] + awayCounts[home];
        var awayGames = homeCounts[away] + awayCounts[away];

        // Prioritize teams that are behind in total assigned games to reduce concentrated shortfalls.
        var teamVolumePenalty = (homeGames + awayGames) * 20;
        var teamImbalancePenalty = Math.Abs(homeGames - awayGames) * 5;
        var teamLoadSpreadPenalty = TeamLoadSpreadAfterAssignment(home, away, teams, homeCounts, awayCounts) * 100;
        var homeAwayPenalty = 0;

        if (balanceHomeAway)
        {
            var homeDiff = Math.Abs((homeCounts[home] + 1) - awayCounts[home]);
            var awayDiff = Math.Abs((awayCounts[away] + 1) - homeCounts[away]);
            homeAwayPenalty = homeDiff + awayDiff;
        }

        return new ScheduleScoreBreakdown(
            TeamVolumePenalty: teamVolumePenalty,
            TeamImbalancePenalty: teamImbalancePenalty,
            TeamLoadSpreadPenalty: teamLoadSpreadPenalty,
            HomeAwayPenalty: homeAwayPenalty,
            TotalScore: teamVolumePenalty + teamImbalancePenalty + teamLoadSpreadPenalty + homeAwayPenalty);
    }

    private static int TeamLoadSpreadAfterAssignment(
        string home,
        string away,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts)
    {
        if (teams.Count == 0) return 0;

        var min = int.MaxValue;
        var max = int.MinValue;

        foreach (var team in teams)
        {
            var total = homeCounts[team] + awayCounts[team];
            if (string.Equals(team, home, StringComparison.OrdinalIgnoreCase)) total += 1;
            if (string.Equals(team, away, StringComparison.OrdinalIgnoreCase)) total += 1;
            if (total < min) min = total;
            if (total > max) max = total;
        }

        if (min == int.MaxValue || max == int.MinValue) return 0;
        return max - min;
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

    private static string? GetConstraintRejectReason(
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
            if (gamesByDate[home].Contains(gameDate)) return $"double-header:{home}";
            if (gamesByDate[away].Contains(gameDate)) return $"double-header:{away}";
        }

        if (maxGamesPerWeek.HasValue)
        {
            var weekKey = WeekKey(gameDate);
            if (!string.IsNullOrWhiteSpace(weekKey))
            {
                if (GetWeekCount(gamesByWeek, home, weekKey) >= maxGamesPerWeek.Value) return $"max-games-per-week:{home}:{weekKey}";
                if (GetWeekCount(gamesByWeek, away, weekKey) >= maxGamesPerWeek.Value) return $"max-games-per-week:{away}:{weekKey}";
            }
        }

        return null;
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

    private static string PickExternalHome(IReadOnlyList<string> teams, Dictionary<string, int> homeCounts, Dictionary<string, int> awayCounts)
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
}
