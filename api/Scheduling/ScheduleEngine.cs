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
    int WeeklyParticipationPenalty,
    int PairRepeatPenalty,
    int IdleGapReductionBonus,
    int LatePriorityPenalty,
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
        bool includePlacementTraces = false,
        int? tieBreakSeed = null,
        IReadOnlyDictionary<string, int>? matchupPriorityByPair = null)
    {
        var teamSet = new HashSet<string>(teams, StringComparer.OrdinalIgnoreCase);
        var homeCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var awayCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var gamesByDate = teams.ToDictionary(t => t, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var gamesByWeek = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pairCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var gamesByTeamDates = teams.ToDictionary(t => t, _ => new List<DateTime>(), StringComparer.OrdinalIgnoreCase);
        var externalOfferCounts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var slotDateRange = BuildSlotDateRange(slots);

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
                    slot.SlotId,
                    fixedHome,
                    remainingMatchups,
                    teams,
                    homeCounts,
                    awayCounts,
                    gamesByDate,
                    gamesByWeek,
                    pairCounts,
                    gamesByTeamDates,
                    matchupPriorityByPair,
                    slotDateRange,
                    constraints.MaxGamesPerWeek,
                    constraints.NoDoubleHeaders,
                    constraints.BalanceHomeAway,
                    tieBreakSeed);
                pick = tracedPick.Pick;
                selectedScoreBreakdown = tracedPick.SelectedScoreBreakdown;
                topFeasibleAlternatives = tracedPick.TopFeasibleAlternatives;
                topRejectedAlternatives = tracedPick.TopRejectedAlternatives;
                candidateCount = tracedPick.CandidateCount;
                feasibleCandidateCount = tracedPick.FeasibleCandidateCount;
            }
            else
            {
                pick = PickMatchup(slot.GameDate, slot.SlotId, fixedHome, remainingMatchups, teams, homeCounts, awayCounts, gamesByDate, gamesByWeek, pairCounts, gamesByTeamDates, matchupPriorityByPair, slotDateRange, constraints.MaxGamesPerWeek, constraints.NoDoubleHeaders, constraints.BalanceHomeAway, tieBreakSeed);
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
            ApplyCounts(home, away, slot.GameDate, homeCounts, awayCounts, gamesByDate, gamesByWeek, pairCounts, gamesByTeamDates);
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

                var externalOffersAssignedThisWeek = 0;
                foreach (var slot in group)
                {
                    if (externalOffersAssignedThisWeek >= constraints.ExternalOfferPerWeek)
                    {
                        remaining.Add(slot);
                        continue;
                    }

                    var home = PickExternalHome(
                        teams,
                        slot.SlotId,
                        slot.GameDate,
                        homeCounts,
                        awayCounts,
                        gamesByDate,
                        gamesByWeek,
                        externalOfferCounts,
                        constraints.MaxGamesPerWeek,
                        constraints.NoDoubleHeaders,
                        tieBreakSeed);
                    if (string.IsNullOrWhiteSpace(home))
                    {
                        remaining.Add(slot);
                        continue;
                    }

                    ApplyCounts(home, "", slot.GameDate, homeCounts, awayCounts, gamesByDate, gamesByWeek, pairCounts, gamesByTeamDates);
                    externalOfferCounts[home] = externalOfferCounts.TryGetValue(home, out var externalCount) ? externalCount + 1 : 1;
                    assignments.Add(new ScheduleAssignment(slot.SlotId, slot.GameDate, slot.StartTime, slot.EndTime, slot.FieldKey, home, "", true));
                    externalOffersAssignedThisWeek += 1;
                }
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
        string slotId,
        string fixedHome,
        List<MatchupPair> matchups,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        Dictionary<string, int> pairCounts,
        Dictionary<string, List<DateTime>> gamesByTeamDates,
        IReadOnlyDictionary<string, int>? matchupPriorityByPair,
        SlotDateRange? slotDateRange,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        int? tieBreakSeed)
    {
        MatchupPair? best = null;
        var bestScore = int.MaxValue;
        var bestTieBreak = int.MaxValue;

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

            var score = ScoreCandidate(home, away, gameDate, teams, homeCounts, awayCounts, gamesByWeek, pairCounts, gamesByTeamDates, matchupPriorityByPair, slotDateRange, balanceHomeAway);
            var tieBreakValue = tieBreakSeed.HasValue
                ? ComputeSeededTieBreak(tieBreakSeed.Value, slotId, gameDate, home, away)
                : int.MaxValue;

            if (score < bestScore)
            {
                bestScore = score;
                bestTieBreak = tieBreakValue;
                best = new MatchupPair(home, away);
                if (score == 0 && !tieBreakSeed.HasValue) break;
            }
            else if (score == bestScore && tieBreakSeed.HasValue && tieBreakValue < bestTieBreak)
            {
                bestTieBreak = tieBreakValue;
                best = new MatchupPair(home, away);
            }
        }

        return best;
    }

    private static MatchupPickTraceResult PickMatchupWithTrace(
        string gameDate,
        string slotId,
        string fixedHome,
        List<MatchupPair> matchups,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        Dictionary<string, int> pairCounts,
        Dictionary<string, List<DateTime>> gamesByTeamDates,
        IReadOnlyDictionary<string, int>? matchupPriorityByPair,
        SlotDateRange? slotDateRange,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        bool balanceHomeAway,
        int? tieBreakSeed)
    {
        MatchupPair? best = null;
        ScheduleScoreBreakdown? bestBreakdown = null;
        var bestScore = int.MaxValue;
        var bestTieBreak = int.MaxValue;
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
            var breakdown = ScoreCandidateBreakdown(home, away, gameDate, teams, homeCounts, awayCounts, gamesByWeek, pairCounts, gamesByTeamDates, matchupPriorityByPair, slotDateRange, balanceHomeAway);
            candidates.Add(new ScheduleCandidateTrace(
                HomeTeamId: home,
                AwayTeamId: away,
                Feasible: true,
                RejectReason: null,
                ScoreBreakdown: breakdown));

            var tieBreakValue = tieBreakSeed.HasValue
                ? ComputeSeededTieBreak(tieBreakSeed.Value, slotId, gameDate, home, away)
                : int.MaxValue;

            if (breakdown.TotalScore < bestScore)
            {
                bestScore = breakdown.TotalScore;
                bestTieBreak = tieBreakValue;
                bestBreakdown = breakdown;
                best = new MatchupPair(home, away);
                if (bestScore == 0 && !tieBreakSeed.HasValue) break;
            }
            else if (breakdown.TotalScore == bestScore && tieBreakSeed.HasValue && tieBreakValue < bestTieBreak)
            {
                bestTieBreak = tieBreakValue;
                bestBreakdown = breakdown;
                best = new MatchupPair(home, away);
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
        string gameDate,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, int> gamesByWeek,
        Dictionary<string, int> pairCounts,
        Dictionary<string, List<DateTime>> gamesByTeamDates,
        IReadOnlyDictionary<string, int>? matchupPriorityByPair,
        SlotDateRange? slotDateRange,
        bool balanceHomeAway)
    {
        return ScoreCandidateBreakdown(home, away, gameDate, teams, homeCounts, awayCounts, gamesByWeek, pairCounts, gamesByTeamDates, matchupPriorityByPair, slotDateRange, balanceHomeAway).TotalScore;
    }

    private static ScheduleScoreBreakdown ScoreCandidateBreakdown(
        string home,
        string away,
        string gameDate,
        IReadOnlyList<string> teams,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, int> gamesByWeek,
        Dictionary<string, int> pairCounts,
        Dictionary<string, List<DateTime>> gamesByTeamDates,
        IReadOnlyDictionary<string, int>? matchupPriorityByPair,
        SlotDateRange? slotDateRange,
        bool balanceHomeAway)
    {
        var homeGames = homeCounts[home] + awayCounts[home];
        var awayGames = homeCounts[away] + awayCounts[away];

        // Prioritize teams that are behind in total assigned games to reduce concentrated shortfalls.
        var teamVolumePenalty = (homeGames + awayGames) * 20;
        var teamImbalancePenalty = Math.Abs(homeGames - awayGames) * 5;
        var teamLoadSpreadPenalty = TeamLoadSpreadAfterAssignment(home, away, teams, homeCounts, awayCounts) * 100;
        var weeklyParticipationPenalty = WeeklyParticipationPenaltyAfterAssignment(home, away, gameDate, gamesByWeek);
        var pairRepeatPenalty = PairRepeatPenaltyAfterAssignment(home, away, pairCounts);
        var idleGapReductionBonus = IdleGapReductionBonusAfterAssignment(home, away, gameDate, gamesByTeamDates);
        var latePriorityPenalty = LatePriorityPenaltyAfterAssignment(home, away, gameDate, matchupPriorityByPair, slotDateRange);
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
            WeeklyParticipationPenalty: weeklyParticipationPenalty,
            PairRepeatPenalty: pairRepeatPenalty,
            IdleGapReductionBonus: idleGapReductionBonus,
            LatePriorityPenalty: latePriorityPenalty,
            HomeAwayPenalty: homeAwayPenalty,
            TotalScore: teamVolumePenalty + teamImbalancePenalty + teamLoadSpreadPenalty + weeklyParticipationPenalty + pairRepeatPenalty - idleGapReductionBonus + latePriorityPenalty + homeAwayPenalty);
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

    private static int WeeklyParticipationPenaltyAfterAssignment(
        string home,
        string away,
        string gameDate,
        Dictionary<string, int> gamesByWeek)
    {
        var weekKey = WeekKey(gameDate);
        if (string.IsNullOrWhiteSpace(weekKey)) return 0;

        var homeWeekCount = GetWeekCount(gamesByWeek, home, weekKey);
        var awayWeekCount = GetWeekCount(gamesByWeek, away, weekKey);

        // Explicitly prefer giving teams their first game in a week (0 -> 1) over stacking second/third games.
        // This complements total-season balance and makes "1 game > 0 games" visible in scoring traces.
        var homePenalty = homeWeekCount * homeWeekCount * 40;
        var awayPenalty = awayWeekCount * awayWeekCount * 40;
        return homePenalty + awayPenalty;
    }

    private static int PairRepeatPenaltyAfterAssignment(
        string home,
        string away,
        Dictionary<string, int> pairCounts)
    {
        var pairKey = PairKey(home, away);
        if (string.IsNullOrWhiteSpace(pairKey)) return 0;
        var existing = pairCounts.TryGetValue(pairKey, out var count) ? count : 0;

        // Strongly prefer fresh pairings before repeats; quadratic growth makes repeated pairs expensive.
        return existing * existing * 60;
    }

    private static int IdleGapReductionBonusAfterAssignment(
        string home,
        string away,
        string gameDate,
        Dictionary<string, List<DateTime>> gamesByTeamDates)
    {
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var currentDate))
            return 0;

        return IdleGapReductionBonusForTeam(home, currentDate, gamesByTeamDates)
            + IdleGapReductionBonusForTeam(away, currentDate, gamesByTeamDates);
    }

    private static int IdleGapReductionBonusForTeam(
        string teamId,
        DateTime currentDate,
        Dictionary<string, List<DateTime>> gamesByTeamDates)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return 0;
        if (!gamesByTeamDates.TryGetValue(teamId, out var dates) || dates.Count == 0) return 0;

        var nearestGapDays = int.MaxValue;
        foreach (var existing in dates)
        {
            var gap = Math.Abs((existing.Date - currentDate.Date).Days);
            if (gap < nearestGapDays) nearestGapDays = gap;
        }

        if (nearestGapDays == int.MaxValue) return 0;

        // Reward filling longer-than-weekly gaps. Weekly cadence (~7 days) gets no bonus.
        var extraWeeksOfGap = Math.Max(0, (nearestGapDays - 7) / 7);
        return Math.Min(4, extraWeeksOfGap) * 20;
    }

    private static int LatePriorityPenaltyAfterAssignment(
        string home,
        string away,
        string gameDate,
        IReadOnlyDictionary<string, int>? matchupPriorityByPair,
        SlotDateRange? slotDateRange)
    {
        if (matchupPriorityByPair is null || matchupPriorityByPair.Count == 0) return 0;
        if (slotDateRange is null) return 0;
        if (!TryGetMatchupPriority(home, away, matchupPriorityByPair, out var priorityWeight) || priorityWeight <= 0) return 0;
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var currentDate))
            return 0;

        var totalDays = Math.Max(0, (slotDateRange.Value.MaxDate.Date - slotDateRange.Value.MinDate.Date).Days);
        if (totalDays <= 0) return 0;
        var daysFromStart = Math.Clamp((currentDate.Date - slotDateRange.Value.MinDate.Date).Days, 0, totalDays);
        var earlinessDays = totalDays - daysFromStart;

        // Higher-priority pairings should prefer later dates; earlier placements pay a proportional penalty.
        return (priorityWeight * earlinessDays * 20) / totalDays;
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

    private static int ComputeSeededTieBreak(int seed, string slotId, string gameDate, string home, string away)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in $"{slotId}|{gameDate}|{home}|{away}")
            {
                hash = (hash * 31) + ch;
            }

            var normalizedSeed = seed == int.MinValue ? 0 : seed;
            var mixed = hash ^ (normalizedSeed * 16777619);
            mixed ^= (mixed >> 16);
            mixed *= 224682251;
            mixed ^= (mixed >> 13);
            return mixed;
        }
    }

    private static void ApplyCounts(
        string home,
        string away,
        string gameDate,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        Dictionary<string, int> pairCounts,
        Dictionary<string, List<DateTime>> gamesByTeamDates)
    {
        if (!string.IsNullOrWhiteSpace(home))
        {
            homeCounts[home] += 1;
            gamesByDate[home].Add(gameDate);
            AddWeekCount(gamesByWeek, home, gameDate);
            AddTeamGameDate(gamesByTeamDates, home, gameDate);
        }
        if (!string.IsNullOrWhiteSpace(away))
        {
            awayCounts[away] += 1;
            gamesByDate[away].Add(gameDate);
            AddWeekCount(gamesByWeek, away, gameDate);
            AddTeamGameDate(gamesByTeamDates, away, gameDate);
        }
        if (!string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            var pairKey = PairKey(home, away);
            if (!string.IsNullOrWhiteSpace(pairKey))
            {
                pairCounts[pairKey] = pairCounts.TryGetValue(pairKey, out var count) ? count + 1 : 1;
            }
        }
    }

    private static string PickExternalHome(
        IReadOnlyList<string> teams,
        string slotId,
        string gameDate,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        Dictionary<string, int> externalOfferCounts,
        int? maxGamesPerWeek,
        bool noDoubleHeaders,
        int? tieBreakSeed)
    {
        string bestTeam = "";
        var bestScore = int.MaxValue;
        var bestTieBreak = int.MaxValue;

        foreach (var team in teams)
        {
            if (!CanAssignExternalHome(team, gameDate, gamesByDate, gamesByWeek, maxGamesPerWeek, noDoubleHeaders))
                continue;

            var score = ScoreExternalOfferCandidate(team, gameDate, homeCounts, awayCounts, gamesByWeek, externalOfferCounts);
            var tieBreakValue = tieBreakSeed.HasValue
                ? ComputeSeededTieBreak(tieBreakSeed.Value, slotId, gameDate, team, "__EXTERNAL__")
                : int.MaxValue;

            if (score < bestScore)
            {
                bestTeam = team;
                bestScore = score;
                bestTieBreak = tieBreakValue;
            }
            else if (score == bestScore)
            {
                if (tieBreakSeed.HasValue)
                {
                    if (tieBreakValue < bestTieBreak)
                    {
                        bestTeam = team;
                        bestTieBreak = tieBreakValue;
                    }
                }
                else if (string.Compare(team, bestTeam, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    bestTeam = team;
                }
            }
        }

        return bestTeam;
    }

    private static bool CanAssignExternalHome(
        string home,
        string gameDate,
        Dictionary<string, HashSet<string>> gamesByDate,
        Dictionary<string, int> gamesByWeek,
        int? maxGamesPerWeek,
        bool noDoubleHeaders)
    {
        if (noDoubleHeaders && gamesByDate[home].Contains(gameDate)) return false;

        if (maxGamesPerWeek.HasValue)
        {
            var weekKey = WeekKey(gameDate);
            if (!string.IsNullOrWhiteSpace(weekKey) && GetWeekCount(gamesByWeek, home, weekKey) >= maxGamesPerWeek.Value)
                return false;
        }

        return true;
    }

    private static int ScoreExternalOfferCandidate(
        string home,
        string gameDate,
        Dictionary<string, int> homeCounts,
        Dictionary<string, int> awayCounts,
        Dictionary<string, int> gamesByWeek,
        Dictionary<string, int> externalOfferCounts)
    {
        var totalGames = homeCounts[home] + awayCounts[home];
        var weekKey = WeekKey(gameDate);
        var weekCount = string.IsNullOrWhiteSpace(weekKey) ? 0 : GetWeekCount(gamesByWeek, home, weekKey);
        var existingExternalOffers = externalOfferCounts.TryGetValue(home, out var count) ? count : 0;

        // Prefer teams with lighter overall load and avoid concentrating external/guest offers on one team.
        var teamVolumePenalty = totalGames * 20;
        var weeklyParticipationPenalty = weekCount * weekCount * 40;
        var externalOfferRepeatPenalty = existingExternalOffers * existingExternalOffers * 80;
        return teamVolumePenalty + weeklyParticipationPenalty + externalOfferRepeatPenalty;
    }

    private static SlotDateRange? BuildSlotDateRange(IReadOnlyList<ScheduleSlot> slots)
    {
        DateTime? minDate = null;
        DateTime? maxDate = null;
        foreach (var slot in slots)
        {
            if (!DateTime.TryParseExact(slot.GameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                continue;
            var date = dt.Date;
            minDate = !minDate.HasValue || date < minDate.Value ? date : minDate;
            maxDate = !maxDate.HasValue || date > maxDate.Value ? date : maxDate;
        }

        return minDate.HasValue && maxDate.HasValue ? new SlotDateRange(minDate.Value, maxDate.Value) : null;
    }

    private static bool TryGetMatchupPriority(string home, string away, IReadOnlyDictionary<string, int> matchupPriorityByPair, out int priorityWeight)
    {
        priorityWeight = 0;
        var pairKey = PairKey(home, away);
        if (string.IsNullOrWhiteSpace(pairKey)) return false;
        if (!matchupPriorityByPair.TryGetValue(pairKey, out var weight)) return false;
        priorityWeight = Math.Max(0, weight);
        return priorityWeight > 0;
    }

    private static string WeekKey(string gameDate)
    {
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return "";
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{dt.Year}-W{week:D2}";
    }

    private static string PairKey(string teamA, string teamB)
    {
        if (string.IsNullOrWhiteSpace(teamA) || string.IsNullOrWhiteSpace(teamB)) return "";
        return string.Compare(teamA, teamB, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{teamA}|{teamB}"
            : $"{teamB}|{teamA}";
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

    private static void AddTeamGameDate(Dictionary<string, List<DateTime>> gamesByTeamDates, string teamId, string gameDate)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return;
        if (!DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return;

        if (!gamesByTeamDates.TryGetValue(teamId, out var dates))
        {
            dates = new List<DateTime>();
            gamesByTeamDates[teamId] = dates;
        }

        dates.Add(dt.Date);
    }

    private readonly record struct SlotDateRange(DateTime MinDate, DateTime MaxDate);
}
