using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class SchedulingTests
{
    [Fact]
    public void ExpandSlots_RespectsExceptions()
    {
        var rule = new AvailabilityRule(
            Division: "10U",
            FieldKey: "park/field",
            StartsOn: new DateOnly(2025, 4, 1),
            EndsOn: new DateOnly(2025, 4, 7),
            DaysOfWeek: new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Monday },
            StartTimeLocal: "18:00",
            EndTimeLocal: "20:00");

        var exceptions = new List<AvailabilityException>
        {
            new(new DateOnly(2025, 4, 3), new DateOnly(2025, 4, 3), "00:00", "23:59")
        };

        var slots = AvailabilityRuleEngine.ExpandSlots(rule, exceptions, gameLengthMinutes: 60);

        Assert.Equal(4, slots.Count);
        Assert.DoesNotContain(slots, s => s.GameDate == new DateOnly(2025, 4, 3));
    }

    [Fact]
    public void BuildRoundRobin_GeneratesExpectedMatchups()
    {
        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = ScheduleEngine.BuildRoundRobin(teams);

        Assert.Equal(6, matchups.Count);
        Assert.Contains(matchups, m =>
            (m.HomeTeamId == "A" && m.AwayTeamId == "B") ||
            (m.HomeTeamId == "B" && m.AwayTeamId == "A"));
    }

    [Fact]
    public void AssignMatchups_RespectsNoDoubleHeaders()
    {
        var slots = new List<ScheduleSlot>
        {
            new("slot-1", "2025-04-01", "18:00", "19:00", "park/field", ""),
            new("slot-2", "2025-04-01", "19:30", "20:30", "park/field", ""),
            new("slot-3", "2025-04-08", "18:00", "19:00", "park/field", ""),
            new("slot-4", "2025-04-08", "19:30", "20:30", "park/field", "")
        };

        var teams = new List<string> { "A", "B", "C", "D" };
        var matchups = ScheduleEngine.BuildRoundRobin(teams);
        var constraints = new ScheduleConstraints(null, NoDoubleHeaders: true, BalanceHomeAway: true, ExternalOfferPerWeek: 0);

        var result = ScheduleEngine.AssignMatchups(slots, matchups, teams, constraints);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in result.Assignments)
        {
            if (!string.IsNullOrWhiteSpace(assignment.HomeTeamId))
            {
                Assert.True(seen.Add($"{assignment.GameDate}|{assignment.HomeTeamId}"));
            }
            if (!string.IsNullOrWhiteSpace(assignment.AwayTeamId))
            {
                Assert.True(seen.Add($"{assignment.GameDate}|{assignment.AwayTeamId}"));
            }
        }
    }

    [Fact]
    public void ValidateSchedule_ReturnsExpectedIssues()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new("slot-1", "2025-04-01", "18:00", "19:00", "park/field", "A", "B", false),
            new("slot-2", "2025-04-01", "19:30", "20:30", "park/field", "A", "C", false)
        };

        var result = new ScheduleResult(
            new ScheduleSummary(2, 2, 2, 2, 0, 0, 0),
            assignments,
            new List<ScheduleAssignment>(),
            new List<MatchupPair>());

        var validation = ScheduleValidation.Validate(result, new ScheduleConstraints(1, true, true, 0));

        Assert.Contains(validation.Issues, issue => issue.RuleId == "double-header");
        Assert.Contains(validation.Issues, issue => issue.RuleId == "max-games-per-week");
    }

    [Fact]
    public void BuildCsv_QuotesValuesAndHeaders()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new("slot-1", "2025-04-01", "18:00", "19:00", "park/field,1", "A", "B", false)
        };

        var internalCsv = ScheduleExport.BuildInternalCsv(assignments, "10U");
        var lines = internalCsv.Split('\n');

        Assert.Equal("\"division\",\"gameDate\",\"startTime\",\"endTime\",\"fieldKey\",\"homeTeamId\",\"awayTeamId\",\"isExternalOffer\"", lines[0]);
        Assert.Contains("\"park/field,1\"", lines[1]);

        var sportsCsv = ScheduleExport.BuildSportsEngineCsv(assignments, new Dictionary<string, string>
        {
            ["park/field,1"] = "Main \"Field\""
        });

        Assert.StartsWith("\"Event Type\"", sportsCsv);
        Assert.Contains("\"Main \"\"Field\"\"\"", sportsCsv);
    }

    [Fact]
    public void ScheduleSeedFlow_BuildsSlotsScheduleValidationAndExports()
    {
        var rule = new AvailabilityRule(
            Division: "10U",
            FieldKey: "park/field",
            StartsOn: new DateOnly(2025, 4, 1),
            EndsOn: new DateOnly(2025, 4, 7),
            DaysOfWeek: new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday },
            StartTimeLocal: "18:00",
            EndTimeLocal: "20:00");

        var input = new ScheduleSeedInput(
            Division: "10U",
            FieldKey: "park/field",
            Teams: new List<string> { "A", "B", "C", "D" },
            Rule: rule,
            Exceptions: new List<AvailabilityException>(),
            GameLengthMinutes: 60,
            Constraints: new ScheduleConstraints(2, true, true, 0),
            FieldDisplayNames: new Dictionary<string, string> { ["park/field"] = "Main Field" });

        var result = ScheduleSeedFlow.Run(input);

        Assert.NotEmpty(result.Slots);
        Assert.NotEmpty(result.Schedule.Assignments);
        Assert.True(result.Validation.TotalIssues >= 0);
        Assert.StartsWith("\"division\"", result.InternalCsv);
        Assert.StartsWith("\"Event Type\"", result.SportsEngineCsv);
    }
}
