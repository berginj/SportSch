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

    [Fact]
    public void Propose_ReturnsSwapProposal_ForDoubleHeaderViolation()
    {
        var assignments = new List<ScheduleAssignment>
        {
            A("slot-1", "2026-04-12", "09:00", "10:30", "Field-1", "A", "B"),
            A("slot-2", "2026-04-12", "11:00", "12:30", "Field-2", "A", "C"),
            A("slot-3", "2026-04-19", "11:00", "12:30", "Field-2", "D", "C")
        };
        var result = BuildResult(assignments, new List<ScheduleAssignment>(), new List<MatchupPair>());
        var config = new ScheduleValidationV2Config(MaxGamesPerWeek: 2, NoDoubleHeaders: true, BalanceHomeAway: false);
        var baseline = ScheduleValidationV2.Validate(result, config, new[] { "A", "B", "C", "D" }).RuleHealth;

        var proposals = ScheduleRepairEngine.Propose(result, baseline, config, new[] { "A", "B", "C", "D" }, maxProposals: 10);

        var swap = proposals.FirstOrDefault(p =>
            !p.RequiresUserAction &&
            p.GamesMoved == 2 &&
            p.HardViolationsResolved >= 1 &&
            p.Changes.Count >= 2 &&
            p.Changes.All(c => c.ChangeType == "move"));
        Assert.NotNull(swap);
        Assert.Contains(swap!.FixesRuleIds, r => r == "double-header");
    }

    [Fact]
    public void Propose_PrefersPriorityPairMoveLater_WhenHardFixImpactIsEquivalent()
    {
        var assignments = new List<ScheduleAssignment>
        {
            A("used-1", "2026-04-12", "10:00", "12:00", "Field-1", "A", "B")
        };
        var unusedSlots = new List<ScheduleAssignment>
        {
            Empty("open-earlier", "2026-04-05", "10:00", "12:00", "Field-2"),
            Empty("open-later", "2026-04-19", "10:00", "12:00", "Field-2")
        };
        var result = BuildResult(assignments, unusedSlots, new List<MatchupPair>());
        var config = new ScheduleValidationV2Config(
            MaxGamesPerWeek: 2,
            NoDoubleHeaders: true,
            BalanceHomeAway: false,
            BlackoutWindows: new[]
            {
                new ScheduleBlackoutWindow("spring-break", new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 14), "Spring Break")
            },
            MatchupPriorityByPair: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["A|B"] = 5
            });
        var baseline = ScheduleValidationV2.Validate(result, config, new[] { "A", "B" }).RuleHealth;

        var proposals = ScheduleRepairEngine.Propose(result, baseline, config, new[] { "A", "B" }, maxProposals: 10);

        var moveProposals = proposals
            .Where(p => !p.RequiresUserAction && p.FixesRuleIds.Contains("blackout-window"))
            .Where(p => p.Changes.Any(c => c.ToSlotId == "open-earlier" || c.ToSlotId == "open-later"))
            .ToList();

        Assert.True(moveProposals.Count >= 2);
        var firstMove = moveProposals[0];
        Assert.Contains(firstMove.Changes, c => c.ToSlotId == "open-later");
        Assert.Contains("Priority pair impact", firstMove.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.True(firstMove.BeforeAfterSummary.TryGetValue("priorityPairsLater", out var laterRaw));
        Assert.Equal(1, Convert.ToInt32(laterRaw));
    }

    [Fact]
    public void Propose_ReturnsThreeGameRotationProposal_ForDoubleHeaderRepair()
    {
        var assignments = new List<ScheduleAssignment>
        {
            A("slot-1", "2026-04-12", "09:00", "10:30", "Field-1", "A", "B"), // source to move off date
            A("slot-a2", "2026-04-12", "11:00", "12:30", "Field-2", "A", "J"), // causes A double-header
            A("slot-2", "2026-04-19", "09:00", "10:30", "Field-1", "C", "D"), // second rotation leg
            A("slot-3", "2026-04-26", "09:00", "10:30", "Field-1", "E", "F"), // third rotation leg
            A("slot-c1", "2026-04-12", "13:00", "14:30", "Field-3", "C", "H"), // blocks some direct swaps
            A("slot-b1", "2026-04-26", "13:00", "14:30", "Field-3", "B", "I"),
        };
        var result = BuildResult(assignments, new List<ScheduleAssignment>(), new List<MatchupPair>());
        var config = new ScheduleValidationV2Config(MaxGamesPerWeek: 2, NoDoubleHeaders: true, BalanceHomeAway: false);
        var baseline = ScheduleValidationV2.Validate(result, config, new[] { "A", "B", "C", "D", "E", "F", "H", "I", "J" }).RuleHealth;

        Assert.Contains(baseline.Groups, g => g.RuleId == "double-header" && g.Severity == "hard");

        var proposals = ScheduleRepairEngine.Propose(
            result,
            baseline,
            config,
            new[] { "A", "B", "C", "D", "E", "F", "H", "I", "J" },
            maxProposals: 200);

        var rotation = proposals.FirstOrDefault(p =>
            !p.RequiresUserAction &&
            p.GamesMoved == 3 &&
            p.Changes.Count == 3 &&
            p.Changes.All(c => c.ChangeType == "move") &&
            p.BeforeAfterSummary.TryGetValue("repairMoveType", out var kind) &&
            string.Equals(Convert.ToString(kind), "rotate3", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(rotation);
        Assert.Contains(rotation!.FixesRuleIds, r => r == "double-header");
        Assert.True(rotation.HardViolationsResolved >= 1);
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
