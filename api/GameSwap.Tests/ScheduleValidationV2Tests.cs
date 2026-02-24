using System;
using System.Collections.Generic;
using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class ScheduleValidationV2Tests
{
    [Fact]
    public void Validate_FlagsBlackoutAsHardViolation()
    {
        var result = BuildResult(assignments: new[]
        {
            A("s1", "2026-04-05", "09:00", "10:30", "Field-1", "A", "B")
        });

        var report = ScheduleValidationV2.Validate(
            result,
            new ScheduleValidationV2Config(
                MaxGamesPerWeek: 1,
                NoDoubleHeaders: true,
                BalanceHomeAway: true,
                BlackoutWindows: new[]
                {
                    new ScheduleBlackoutWindow("spring-break", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 7), "Spring Break")
                }),
            teams: new[] { "A", "B" }).RuleHealth;

        Assert.True(report.ApplyBlocked);
        Assert.Equal("red", report.Status);
        Assert.Contains(report.Groups, g => g.RuleId == "blackout-window" && g.Severity == "hard");
    }

    [Fact]
    public void Validate_FlagsNoDoubleheaderAsHardViolation()
    {
        var result = BuildResult(assignments: new[]
        {
            A("s1", "2026-04-12", "09:00", "10:30", "Field-1", "A", "B"),
            A("s2", "2026-04-12", "11:00", "12:30", "Field-2", "A", "C")
        });

        var report = ScheduleValidationV2.Validate(
            result,
            new ScheduleValidationV2Config(MaxGamesPerWeek: 2, NoDoubleHeaders: true, BalanceHomeAway: false),
            teams: new[] { "A", "B", "C" }).RuleHealth;

        Assert.True(report.ApplyBlocked);
        Assert.Contains(report.Groups, g => g.RuleId == "double-header" && g.Severity == "hard");
    }

    [Fact]
    public void Validate_FlagsMaxGamesPerWeekAsHardViolation()
    {
        var result = BuildResult(assignments: new[]
        {
            A("s1", "2026-04-14", "09:00", "10:30", "Field-1", "A", "B"),
            A("s2", "2026-04-16", "09:00", "10:30", "Field-2", "A", "C")
        });

        var report = ScheduleValidationV2.Validate(
            result,
            new ScheduleValidationV2Config(MaxGamesPerWeek: 1, NoDoubleHeaders: false, BalanceHomeAway: false),
            teams: new[] { "A", "B", "C" }).RuleHealth;

        Assert.True(report.ApplyBlocked);
        Assert.Contains(report.Groups, g => g.RuleId == "max-games-per-week" && g.Severity == "hard");
    }

    [Fact]
    public void Validate_TreatsUnassignedRequiredMatchupsAsHard_AndBlocksApply()
    {
        var result = BuildResult(unassignedMatchups: new[]
        {
            new MatchupPair("A", "B")
        });

        var report = ScheduleValidationV2.Validate(
            result,
            new ScheduleValidationV2Config(MaxGamesPerWeek: 1, NoDoubleHeaders: true, BalanceHomeAway: false),
            teams: new[] { "A", "B" }).RuleHealth;

        Assert.True(report.ApplyBlocked);
        Assert.Contains(report.Groups, g => g.RuleId == "unscheduled-required-matchups" && g.Severity == "hard");
    }

    [Fact]
    public void Validate_ReportsOpponentRepeatAndIdleGapSoftHealth()
    {
        var result = BuildResult(assignments: new[]
        {
            A("s1", "2026-04-05", "09:00", "10:30", "Field-1", "A", "B"),
            A("s2", "2026-04-26", "09:00", "10:30", "Field-1", "A", "B"), // repeat pair + 3-week gap
            A("s3", "2026-04-12", "09:00", "10:30", "Field-2", "C", "D"),
            A("s4", "2026-04-19", "09:00", "10:30", "Field-2", "C", "D")
        });

        var report = ScheduleValidationV2.Validate(
            result,
            new ScheduleValidationV2Config(
                MaxGamesPerWeek: 2,
                NoDoubleHeaders: true,
                BalanceHomeAway: false,
                MatchupPriorityByPair: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["A|B"] = 3
                }),
            teams: new[] { "A", "B", "C", "D" }).RuleHealth;

        Assert.False(report.ApplyBlocked);
        Assert.Equal("yellow", report.Status);
        Assert.Contains(report.Groups, g => g.RuleId == "opponent-repeat-balance" && g.Severity == "soft");
        Assert.Contains(report.Groups, g => g.RuleId == "idle-gap-balance" && g.Severity == "soft");

        var repeatTerm = Assert.Single(report.ScoreBreakdown, t => t.ObjectiveId == "opponent-repeat-overage");
        var idleGapTerm = Assert.Single(report.ScoreBreakdown, t => t.ObjectiveId == "idle-gap-extra-weeks");
        var latePriorityTerm = Assert.Single(report.ScoreBreakdown, t => t.ObjectiveId == "late-priority-placement");
        Assert.True(repeatTerm.Raw > 0);
        Assert.True(repeatTerm.Weighted > 0);
        Assert.True(idleGapTerm.Raw > 0);
        Assert.True(idleGapTerm.Weighted > 0);
        Assert.True(latePriorityTerm.Raw > 0);
        Assert.True(latePriorityTerm.Weighted > 0);
    }

    private static ScheduleAssignment A(
        string slotId,
        string gameDate,
        string startTime,
        string endTime,
        string fieldKey,
        string homeTeamId,
        string awayTeamId,
        bool isExternalOffer = false)
        => new(slotId, gameDate, startTime, endTime, fieldKey, homeTeamId, awayTeamId, isExternalOffer);

    private static ScheduleResult BuildResult(
        IEnumerable<ScheduleAssignment>? assignments = null,
        IEnumerable<ScheduleAssignment>? unassignedSlots = null,
        IEnumerable<MatchupPair>? unassignedMatchups = null)
    {
        var assigned = assignments is null ? new List<ScheduleAssignment>() : new List<ScheduleAssignment>(assignments);
        var unused = unassignedSlots is null ? new List<ScheduleAssignment>() : new List<ScheduleAssignment>(unassignedSlots);
        var leftover = unassignedMatchups is null ? new List<MatchupPair>() : new List<MatchupPair>(unassignedMatchups);
        var summary = new ScheduleSummary(
            SlotsTotal: assigned.Count + unused.Count,
            SlotsAssigned: assigned.Count,
            MatchupsTotal: assigned.Count + leftover.Count,
            MatchupsAssigned: assigned.Count,
            ExternalOffers: assigned.FindAll(a => a.IsExternalOffer).Count,
            UnassignedSlots: unused.Count,
            UnassignedMatchups: leftover.Count);
        return new ScheduleResult(summary, assigned, unused, leftover);
    }
}
