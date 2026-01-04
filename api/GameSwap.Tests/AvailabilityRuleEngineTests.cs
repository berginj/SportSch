using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class AvailabilityRuleEngineTests
{
    [Fact]
    public void ExpandRecurringSlots_SkipsExceptions()
    {
        var ruleId = "rule-1";
        var rules = new List<AvailabilityRuleSpec>
        {
            new(
                RuleId: ruleId,
                FieldKey: "park/field1",
                Division: "10U",
                StartsOn: new DateOnly(2026, 4, 1),
                EndsOn: new DateOnly(2026, 4, 30),
                Days: new HashSet<DayOfWeek> { DayOfWeek.Monday },
                StartMin: 18 * 60,
                EndMin: 20 * 60)
        };

        var exceptions = new Dictionary<string, List<AvailabilityExceptionSpec>>
        {
            [ruleId] = new List<AvailabilityExceptionSpec>
            {
                new(
                    DateFrom: new DateOnly(2026, 4, 6),
                    DateTo: new DateOnly(2026, 4, 6),
                    StartMin: 18 * 60,
                    EndMin: 19 * 60)
            }
        };

        var slots = AvailabilityRuleEngine.ExpandRecurringSlots(
            rules,
            exceptions,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 30),
            gameLengthMinutes: 60,
            blackouts: new List<(DateOnly start, DateOnly end)>());

        Assert.DoesNotContain(slots, s => s.GameDate == "2026-04-06" && s.StartTime == "18:00" && s.EndTime == "19:00");
        Assert.Contains(slots, s => s.GameDate == "2026-04-06" && s.StartTime == "19:00" && s.EndTime == "20:00");
    }
}
