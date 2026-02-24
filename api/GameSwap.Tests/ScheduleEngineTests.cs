using System;
using System.Collections.Generic;
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
}
