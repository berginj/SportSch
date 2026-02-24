using System;
using System.Collections.Generic;
using System.Linq;
using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class ScheduleRepairEngineTests
{
    [Fact]
    public void Propose_ReturnsMoveProposal_ForBlackoutViolation_WhenUnusedSlotExists()
    {
        var assignments = new List<ScheduleAssignment>
        {
            A("used-1", "2026-04-05", "09:00", "10:30", "Field-1", "A", "B")
        };
        var unusedSlots = new List<ScheduleAssignment>
        {
            Empty("open-1", "2026-04-12", "09:00", "10:30", "Field-2")
        };
        var result = BuildResult(assignments, unusedSlots, new List<MatchupPair>());
        var config = new ScheduleValidationV2Config(
            MaxGamesPerWeek: 1,
            NoDoubleHeaders: true,
            BalanceHomeAway: true,
            BlackoutWindows: new[]
            {
                new ScheduleBlackoutWindow("spring-break", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 7), "Spring Break")
            });
        var baseline = ScheduleValidationV2.Validate(result, config, new[] { "A", "B" }).RuleHealth;

        var proposals = ScheduleRepairEngine.Propose(result, baseline, config, new[] { "A", "B" }, maxProposals: 5);

        var move = proposals.FirstOrDefault(p => !p.RequiresUserAction && p.FixesRuleIds.Contains("blackout-window"));
        Assert.NotNull(move);
        Assert.Equal(1, move!.GamesMoved);
        Assert.Equal(1, move.HardViolationsResolved);
        Assert.Contains(move.Changes, c => c.ChangeType == "move" && c.FromSlotId == "used-1" && c.ToSlotId == "open-1");
    }

    [Fact]
    public void Propose_ReturnsCapacitySuggestion_ForUnscheduledRequiredMatchups_WhenNoUnusedSlots()
    {
        var result = BuildResult(
            new List<ScheduleAssignment>(),
            new List<ScheduleAssignment>(),
            new List<MatchupPair> { new("A", "B") });
        var config = new ScheduleValidationV2Config(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: false);
        var baseline = ScheduleValidationV2.Validate(result, config, new[] { "A", "B" }).RuleHealth;

        var proposals = ScheduleRepairEngine.Propose(result, baseline, config, new[] { "A", "B" }, maxProposals: 5);

        Assert.Contains(proposals, p => p.RequiresUserAction && p.FixesRuleIds.Contains("unscheduled-required-matchups"));
    }

    private static ScheduleAssignment A(string slotId, string date, string start, string end, string field, string home, string away)
        => new(slotId, date, start, end, field, home, away, false);

    private static ScheduleAssignment Empty(string slotId, string date, string start, string end, string field)
        => new(slotId, date, start, end, field, "", "", false);

    private static ScheduleResult BuildResult(
        List<ScheduleAssignment> assignments,
        List<ScheduleAssignment> unusedSlots,
        List<MatchupPair> unassignedMatchups)
    {
        var summary = new ScheduleSummary(
            SlotsTotal: assignments.Count + unusedSlots.Count,
            SlotsAssigned: assignments.Count,
            MatchupsTotal: assignments.Count + unassignedMatchups.Count,
            MatchupsAssigned: assignments.Count,
            ExternalOffers: 0,
            UnassignedSlots: unusedSlots.Count,
            UnassignedMatchups: unassignedMatchups.Count);
        return new ScheduleResult(summary, assignments, unusedSlots, unassignedMatchups);
    }
}
