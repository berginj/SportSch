namespace GameSwap.Functions.Scheduling;

public record ScheduleSeasonPhaseWeight(string PhaseId, double WeatherReliabilityWeight);

public record ScheduleBlackoutWindow(
    string RuleId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Label);

// Stage A scaffold for the backward/repair scheduler pipeline. This is additive and not yet the primary planner input.
public record SchedulingProblem(
    string LeagueId,
    string Division,
    int Seed,
    DateOnly SeasonStart,
    DateOnly SeasonEnd,
    IReadOnlyList<string> Teams,
    IReadOnlyList<ScheduleSlot> Slots,
    IReadOnlyList<MatchupPair> Matchups,
    ScheduleConstraints Constraints,
    IReadOnlyList<ScheduleBlackoutWindow> BlackoutWindows,
    IReadOnlyList<ScheduleSeasonPhaseWeight> PhaseWeights,
    IReadOnlyDictionary<string, int>? MatchupPriorityByPair = null,
    IReadOnlyCollection<string>? NoGamesOnDates = null,
    int? NoGamesBeforeMinute = null,
    int? NoGamesAfterMinute = null);
