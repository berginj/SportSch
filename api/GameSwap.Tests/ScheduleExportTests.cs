using GameSwap.Functions.Scheduling;
using Xunit;

namespace GameSwap.Tests;

public class ScheduleExportTests
{
    [Fact]
    public void BuildSportsEngineCsv_EmitsHeaderAndRow()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new(
                SlotId: "slot-1",
                GameDate: "2026-04-06",
                StartTime: "18:00",
                EndTime: "19:00",
                FieldKey: "park/field1",
                HomeTeamId: "A",
                AwayTeamId: "B",
                IsExternalOffer: false)
        };

        var fields = new Dictionary<string, string> { ["park/field1"] = "Park Field 1" };
        var csv = ScheduleExport.BuildSportsEngineCsv(assignments, fields);

        Assert.Contains("Event Type", csv);
        Assert.Contains("Park Field 1", csv);
        Assert.Contains("2026-04-06", csv);
    }
}
