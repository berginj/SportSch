using System;
using System.Collections.Generic;
using System.Linq;
using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class ScheduleEngineTests
{
    [Fact]
    public void BuildRoundRobin_ForFourTeams_CreatesSixMatchups()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = ScheduleEngine.BuildRoundRobin(teams);

        Assert.Equal(6, matchups.Count);
        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in matchups)
        {
            var ordered = string.CompareOrdinal(m.HomeTeamId, m.AwayTeamId) < 0
                ? $"{m.HomeTeamId}|{m.AwayTeamId}"
                : $"{m.AwayTeamId}|{m.HomeTeamId}";
            pairs.Add(ordered);
        }

        Assert.Equal(6, pairs.Count);
    }

    [Fact]
    public void AssignMatchups_UsesProvidedSlotOrder_SoBackwardOrderedSlotsFillLateDatesFirst()
    {
        var teams = new List<string> { "A", "B" };
        var matchups = new List<MatchupPair> { new("A", "B") };
        var slots = new List<ScheduleSlot>
        {
            // Wizard backward strategy now sends later season slots first for regular-season construction.
            new("late-slot", "2026-05-31", "10:00", "12:00", "Field-1", ""),
            new("early-slot", "2026-03-15", "10:00", "12:00", "Field-1", "")
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: true, ExternalOfferPerWeek: 0);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints);

        Assert.Single(result.Assignments);
        Assert.Equal("late-slot", result.Assignments[0].SlotId);
        Assert.Single(result.UnassignedSlots);
        Assert.Equal("early-slot", result.UnassignedSlots[0].SlotId);
    }

    [Fact]
    public void AssignMatchups_WhenPlacementTracesEnabled_ReturnsDeterministicTraceMetadata()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = ScheduleEngine.BuildRoundRobin(teams);
        var slots = new List<ScheduleSlot>
        {
            new("slot-1", "2026-05-10", "09:00", "11:00", "Field-1", ""),
            new("slot-2", "2026-05-17", "09:00", "11:00", "Field-1", "")
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: true, ExternalOfferPerWeek: 0);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints, includePlacementTraces: true);

        Assert.NotNull(result.PlacementTraces);
        Assert.Equal(slots.Count, result.PlacementTraces!.Count);

        var firstTrace = result.PlacementTraces[0];
        Assert.Equal("slot-1", firstTrace.SlotId);
        Assert.Equal(0, firstTrace.SlotOrderIndex);
        Assert.True(firstTrace.CandidateCount > 0);
        Assert.True(firstTrace.FeasibleCandidateCount > 0);
        Assert.NotNull(firstTrace.SelectedScoreBreakdown);
        Assert.NotEmpty(firstTrace.TopFeasibleAlternatives);
    }

    [Fact]
    public void AssignMatchups_UsesSeededTieBreak_WhenCandidatesHaveEqualScores()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair>
        {
            new("A", "B"),
            new("A", "C"),
            new("A", "D"),
            new("B", "C"),
            new("B", "D"),
            new("C", "D"),
        };
        var slots = new List<ScheduleSlot> { new("slot-1", "2026-05-10", "10:00", "12:00", "Field-1", "") };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 2, NoDoubleHeaders: false, BalanceHomeAway: false, ExternalOfferPerWeek: 0);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var seed = 1; seed <= 12; seed++)
        {
            var result1 = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints, tieBreakSeed: seed);
            var result2 = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints, tieBreakSeed: seed);
            Assert.Single(result1.Assignments);
            Assert.Single(result2.Assignments);
            Assert.Equal(result1.Assignments[0].HomeTeamId, result2.Assignments[0].HomeTeamId);
            Assert.Equal(result1.Assignments[0].AwayTeamId, result2.Assignments[0].AwayTeamId);
            seen.Add($"{result1.Assignments[0].HomeTeamId}|{result1.Assignments[0].AwayTeamId}");
        }

        Assert.True(seen.Count > 1);
    }

    [Fact]
    public void AssignMatchups_PrefersWeeklyCoverageOverStacking_WhenCandidateWouldCreateSecondGameThatWeek()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair>
        {
            new("A", "B"), // Week 1 seeds an A/B repeat pair and keeps totals predictable
            new("A", "C"), // Week 2 slot 1 (forced via offeringTeamId = A)
            new("A", "B"), // Week 2 slot 2 candidate (creates second game in week for A, repeats pair)
            new("B", "D"), // Week 2 slot 2 candidate (gives B/D first game that week)
        };
        var slots = new List<ScheduleSlot>
        {
            new("slot-w1", "2026-04-06", "10:00", "12:00", "Field-1", ""),
            new("slot-w2a", "2026-04-13", "10:00", "12:00", "Field-1", "A"),
            new("slot-w2b", "2026-04-15", "10:00", "12:00", "Field-1", ""),
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 2, NoDoubleHeaders: false, BalanceHomeAway: false, ExternalOfferPerWeek: 0);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints, includePlacementTraces: true);

        Assert.Equal(3, result.Assignments.Count);
        var third = result.Assignments[2];
        Assert.Equal("slot-w2b", third.SlotId);
        Assert.Equal("B", third.HomeTeamId);
        Assert.Equal("D", third.AwayTeamId);

        var trace = Assert.Single(result.PlacementTraces!.FindAll(t => t.SlotId == "slot-w2b"));
        Assert.NotNull(trace.SelectedScoreBreakdown);
        Assert.Equal(0, trace.SelectedScoreBreakdown!.WeeklyParticipationPenalty);
        Assert.Contains(trace.TopFeasibleAlternatives, c => c.HomeTeamId == "A" && c.AwayTeamId == "B" && (c.ScoreBreakdown?.WeeklyParticipationPenalty ?? 0) > 0);
    }

    [Fact]
    public void AssignMatchups_PrefersFreshPairingOverRepeat_WhenOtherwiseComparable()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair>
        {
            new("A", "B"), // slot 1
            new("A", "B"), // slot 2 candidate repeat
            new("C", "D"), // slot 2 candidate fresh pair
        };
        var slots = new List<ScheduleSlot>
        {
            new("slot-1", "2026-05-01", "10:00", "12:00", "Field-1", ""),
            new("slot-2", "2026-05-08", "10:00", "12:00", "Field-1", ""),
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: false, ExternalOfferPerWeek: 0);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints, includePlacementTraces: true);

        Assert.Equal(2, result.Assignments.Count);
        Assert.Equal("slot-2", result.Assignments[1].SlotId);
        Assert.Equal("C", result.Assignments[1].HomeTeamId);
        Assert.Equal("D", result.Assignments[1].AwayTeamId);

        var trace = Assert.Single(result.PlacementTraces!.FindAll(t => t.SlotId == "slot-2"));
        Assert.NotNull(trace.SelectedScoreBreakdown);
        Assert.Equal(0, trace.SelectedScoreBreakdown!.PairRepeatPenalty);
        Assert.Contains(trace.TopFeasibleAlternatives, c => c.HomeTeamId == "A" && c.AwayTeamId == "B" && (c.ScoreBreakdown?.PairRepeatPenalty ?? 0) > 0);
    }

    [Fact]
    public void AssignMatchups_DoesNotCreateExternalOffer_WhenNoTeamIsFeasibleForSlotConstraints()
    {
        var teams = new List<string> { "A", "B" };
        var matchups = new List<MatchupPair> { new("A", "B") };
        var slots = new List<ScheduleSlot>
        {
            new("slot-1", "2026-05-03", "10:00", "12:00", "Field-1", ""),
            new("slot-2", "2026-05-03", "13:00", "15:00", "Field-1", ""),
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: false, ExternalOfferPerWeek: 1);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints);

        Assert.Single(result.Assignments);
        Assert.DoesNotContain(result.Assignments, a => a.IsExternalOffer);
        Assert.Single(result.UnassignedSlots);
        Assert.Equal("slot-2", result.UnassignedSlots[0].SlotId);
    }

    [Fact]
    public void AssignMatchups_SpreadsExternalOffersAcrossTeams_BeforeRepeatingWhenCapacityAllows()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair>();
        var slots = new List<ScheduleSlot>
        {
            new("slot-1", "2026-05-03", "10:00", "12:00", "Field-1", ""),
            new("slot-2", "2026-05-10", "10:00", "12:00", "Field-1", ""),
            new("slot-3", "2026-05-17", "10:00", "12:00", "Field-1", ""),
            new("slot-4", "2026-05-24", "10:00", "12:00", "Field-1", ""),
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: false, ExternalOfferPerWeek: 1);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints);

        Assert.Equal(4, result.Assignments.Count);
        Assert.All(result.Assignments, a => Assert.True(a.IsExternalOffer));
        var externalHomes = new HashSet<string>(result.Assignments.ConvertAll(a => a.HomeTeamId), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(4, externalHomes.Count);
    }

    [Fact]
    public void AssignMatchups_RespectsExternalOfferSeasonCap_PerTeam()
    {
        var teams = new List<string> { "A", "B" };
        var matchups = new List<MatchupPair>();
        var slots = new List<ScheduleSlot>
        {
            new("slot-1", "2026-05-03", "10:00", "12:00", "Field-1", ""),
            new("slot-2", "2026-05-10", "10:00", "12:00", "Field-1", ""),
            new("slot-3", "2026-05-17", "10:00", "12:00", "Field-1", ""),
        };
        var constraints = new ScheduleConstraints(
            MaxGamesPerWeek: 1,
            NoDoubleHeaders: true,
            BalanceHomeAway: false,
            ExternalOfferPerWeek: 1,
            MaxExternalOffersPerTeamSeason: 1);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints);

        Assert.Equal(2, result.Assignments.Count);
        Assert.All(result.Assignments, a => Assert.True(a.IsExternalOffer));
        var homes = result.Assignments.ConvertAll(a => a.HomeTeamId);
        Assert.Contains("A", homes);
        Assert.Contains("B", homes);
        Assert.Single(result.UnassignedSlots);
        Assert.Equal("slot-3", result.UnassignedSlots[0].SlotId);
    }

    [Fact]
    public void BalanceExternalOfferHomes_RebalancesWhenSpreadExceedsOne_AndConstraintsAllow()
    {
        var teams = new List<string> { "A", "B", "C" };
        var assignments = new List<ScheduleAssignment>
        {
            new("ext-1", "2026-05-03", "10:00", "12:00", "Field-1", "A", "", true),
            new("ext-2", "2026-05-10", "10:00", "12:00", "Field-1", "A", "", true),
            new("ext-3", "2026-05-17", "10:00", "12:00", "Field-1", "A", "", true),
            new("ext-4", "2026-05-24", "10:00", "12:00", "Field-1", "A", "", true),
        };

        var balanced = ScheduleEngine.BalanceExternalOfferHomes(
            assignments,
            teams,
            maxGamesPerWeek: 2,
            noDoubleHeaders: true,
            maxExternalOffersPerTeamSeason: null);

        Assert.Equal(assignments.Count, balanced.Count);
        var counts = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in balanced.Where(a => a.IsExternalOffer))
        {
            counts[assignment.HomeTeamId] = counts.TryGetValue(assignment.HomeTeamId, out var value) ? value + 1 : 1;
        }

        var spread = counts.Values.Max() - counts.Values.Min();
        Assert.True(spread <= 1, $"Expected spread <= 1 but got {spread}. Counts: {string.Join(", ", counts.Select(kvp => $"{kvp.Key}:{kvp.Value}"))}");
    }

    [Fact]
    public void BalanceExternalOfferHomes_DoesNotMoveIntoNoDoubleHeaderConflicts()
    {
        var teams = new List<string> { "A", "B", "C" };
        var assignments = new List<ScheduleAssignment>
        {
            // External offers currently concentrated on A
            new("ext-1", "2026-05-03", "10:00", "12:00", "Field-1", "A", "", true),
            new("ext-2", "2026-05-10", "10:00", "12:00", "Field-1", "A", "", true),
            // B and C already have confirmed games on those dates, so no-doubleheaders prevents reassignment
            new("g-1", "2026-05-03", "13:00", "15:00", "Field-2", "B", "C", false),
            new("g-2", "2026-05-10", "13:00", "15:00", "Field-2", "C", "B", false),
        };

        var balanced = ScheduleEngine.BalanceExternalOfferHomes(
            assignments,
            teams,
            maxGamesPerWeek: 2,
            noDoubleHeaders: true,
            maxExternalOffersPerTeamSeason: null);

        var externalHomes = balanced.Where(a => a.IsExternalOffer).Select(a => a.HomeTeamId).ToList();
        Assert.Equal(new List<string> { "A", "A" }, externalHomes);
    }

    [Fact]
    public void AssignMatchups_PrefersFillingLongerIdleGap_WhenOtherwiseComparable()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair>
        {
            new("C", "D"), // later game for C/D (farther from slot-gap)
            new("A", "B"), // later game for A/B (closer to slot-gap)
            new("A", "B"), // slot-gap candidate repeat (short gap)
            new("C", "D"), // slot-gap candidate repeat (long gap)
        };
        var slots = new List<ScheduleSlot>
        {
            new("slot-w13", "2026-06-14", "10:00", "12:00", "Field-1", "C"),
            new("slot-w10", "2026-05-24", "10:00", "12:00", "Field-1", "A"),
            new("slot-gap", "2026-05-03", "10:00", "12:00", "Field-1", ""),
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 2, NoDoubleHeaders: false, BalanceHomeAway: false, ExternalOfferPerWeek: 0);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints, includePlacementTraces: true);

        Assert.Equal(3, result.Assignments.Count);
        var gapAssignment = Assert.Single(result.Assignments.FindAll(a => a.SlotId == "slot-gap"));
        Assert.Equal("C", gapAssignment.HomeTeamId);
        Assert.Equal("D", gapAssignment.AwayTeamId);

        var trace = Assert.Single(result.PlacementTraces!.FindAll(t => t.SlotId == "slot-gap"));
        Assert.NotNull(trace.SelectedScoreBreakdown);
        Assert.True(trace.SelectedScoreBreakdown!.IdleGapReductionBonus > 0);
        Assert.Contains(
            trace.TopFeasibleAlternatives,
            c => c.HomeTeamId == "A" && c.AwayTeamId == "B" &&
                 (c.ScoreBreakdown?.IdleGapReductionBonus ?? 0) < trace.SelectedScoreBreakdown.IdleGapReductionBonus);
    }

    [Fact]
    public void AssignMatchups_PushesPriorityMatchupLater_WhenEarlierAlternativeExists()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair>
        {
            new("A", "B"), // priority matchup
            new("C", "D")
        };
        var slots = new List<ScheduleSlot>
        {
            new("slot-early", "2026-04-05", "10:00", "12:00", "Field-1", ""),
            new("slot-late", "2026-05-31", "10:00", "12:00", "Field-1", "")
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: false, ExternalOfferPerWeek: 0);
        var priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A|B"] = 5 };

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints, includePlacementTraces: true, matchupPriorityByPair: priorities);

        Assert.Equal(2, result.Assignments.Count);
        var early = Assert.Single(result.Assignments.FindAll(a => a.SlotId == "slot-early"));
        var late = Assert.Single(result.Assignments.FindAll(a => a.SlotId == "slot-late"));
        Assert.Equal("C", early.HomeTeamId);
        Assert.Equal("D", early.AwayTeamId);
        Assert.Equal("A", late.HomeTeamId);
        Assert.Equal("B", late.AwayTeamId);

        var earlyTrace = Assert.Single(result.PlacementTraces!.FindAll(t => t.SlotId == "slot-early"));
        Assert.NotNull(earlyTrace.SelectedScoreBreakdown);
        Assert.Equal(0, earlyTrace.SelectedScoreBreakdown!.LatePriorityPenalty);
        Assert.Contains(
            earlyTrace.TopFeasibleAlternatives,
            c => c.HomeTeamId == "A" && c.AwayTeamId == "B" && (c.ScoreBreakdown?.LatePriorityPenalty ?? 0) > 0);
    }

    [Fact]
    public void AssignMatchups_TraceIncludesWeatherReliabilityPenalty_ForSlotDate()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair> { new("A", "B"), new("C", "D") };
        var slots = new List<ScheduleSlot>
        {
            new("slot-early", "2026-04-05", "10:00", "12:00", "Field-1", ""),
            new("slot-late", "2026-05-31", "10:00", "12:00", "Field-1", "")
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: false, ExternalOfferPerWeek: 0);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints, includePlacementTraces: true);

        var earlyTrace = Assert.Single(result.PlacementTraces!.FindAll(t => t.SlotId == "slot-early"));
        var lateTrace = Assert.Single(result.PlacementTraces!.FindAll(t => t.SlotId == "slot-late"));
        Assert.NotNull(earlyTrace.SelectedScoreBreakdown);
        Assert.NotNull(lateTrace.SelectedScoreBreakdown);
        Assert.True(earlyTrace.SelectedScoreBreakdown!.WeatherReliabilityPenalty > lateTrace.SelectedScoreBreakdown!.WeatherReliabilityPenalty);
    }

    [Fact]
    public void ReplayPlacementTracesForSnapshot_UsesForcedSnapshotAssignments()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = new List<MatchupPair>
        {
            new("A", "B"),
            new("C", "D")
        };
        var slots = new List<ScheduleSlot>
        {
            new("slot-early", "2026-04-05", "10:00", "12:00", "Field-1", ""),
            new("slot-late", "2026-05-31", "10:00", "12:00", "Field-1", "")
        };
        var constraints = new ScheduleConstraints(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: false, ExternalOfferPerWeek: 0);

        // Snapshot intentionally differs from the engine's preferred late-priority placement.
        var snapshotAssignments = new List<ScheduleAssignment>
        {
            new("slot-early", "2026-04-05", "10:00", "12:00", "Field-1", "A", "B", false),
            new("slot-late", "2026-05-31", "10:00", "12:00", "Field-1", "C", "D", false),
        };

        var traces = ScheduleEngine.ReplayPlacementTracesForSnapshot(
            slots,
            matchups,
            snapshotAssignments,
            teams,
            constraints,
            matchupPriorityByPair: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A|B"] = 5 });

        Assert.Equal(2, traces.Count);
        var earlyTrace = Assert.Single(traces.FindAll(t => t.SlotId == "slot-early"));
        Assert.Equal("A", earlyTrace.SelectedHomeTeamId);
        Assert.Equal("B", earlyTrace.SelectedAwayTeamId);
        Assert.NotNull(earlyTrace.SelectedScoreBreakdown);
        Assert.Contains(earlyTrace.TopFeasibleAlternatives, c => c.HomeTeamId == "C" && c.AwayTeamId == "D");
        Assert.True(earlyTrace.SelectedScoreBreakdown!.LatePriorityPenalty > 0);
    }
}
