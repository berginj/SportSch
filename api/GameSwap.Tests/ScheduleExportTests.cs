using System.Collections.Generic;
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

    [Fact]
    public void BuildGameChangerCsv_EmitsCorrectHeaders()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new(
                SlotId: "slot-1",
                GameDate: "2026-04-06",
                StartTime: "18:00",
                EndTime: "19:00",
                FieldKey: "park/field1",
                HomeTeamId: "TeamA",
                AwayTeamId: "TeamB",
                IsExternalOffer: false)
        };

        var fieldDetails = new Dictionary<string, FieldDetails>
        {
            ["park/field1"] = new FieldDetails("Oak Park", "Field 1")
        };

        var csv = ScheduleExport.BuildGameChangerCsv(assignments, fieldDetails);

        // Check for quoted CSV headers
        Assert.Contains("\"Date\"", csv);
        Assert.Contains("\"Time\"", csv);
        Assert.Contains("\"Home Team\"", csv);
        Assert.Contains("\"Away Team\"", csv);
        Assert.Contains("\"Location\"", csv);
        Assert.Contains("\"Field\"", csv);
        Assert.Contains("\"Game Type\"", csv);
        Assert.Contains("\"Game Number\"", csv);
    }

    [Fact]
    public void BuildGameChangerCsv_ConvertsDatesToMMDDYYYY()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new(
                SlotId: "slot-1",
                GameDate: "2026-04-06",
                StartTime: "18:00",
                EndTime: "19:00",
                FieldKey: "park/field1",
                HomeTeamId: "TeamA",
                AwayTeamId: "TeamB",
                IsExternalOffer: false)
        };

        var fieldDetails = new Dictionary<string, FieldDetails>
        {
            ["park/field1"] = new FieldDetails("Oak Park", "Field 1")
        };

        var csv = ScheduleExport.BuildGameChangerCsv(assignments, fieldDetails);

        // Date should be converted from 2026-04-06 to 04/06/2026
        Assert.Contains("04/06/2026", csv);
        Assert.DoesNotContain("2026-04-06", csv);
    }

    [Fact]
    public void BuildGameChangerCsv_Converts24HourTimeTo12Hour()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new(
                SlotId: "slot-1",
                GameDate: "2026-04-06",
                StartTime: "18:00",
                EndTime: "19:00",
                FieldKey: "park/field1",
                HomeTeamId: "TeamA",
                AwayTeamId: "TeamB",
                IsExternalOffer: false)
        };

        var fieldDetails = new Dictionary<string, FieldDetails>
        {
            ["park/field1"] = new FieldDetails("Oak Park", "Field 1")
        };

        var csv = ScheduleExport.BuildGameChangerCsv(assignments, fieldDetails);

        // Time should be converted from 18:00 to 6:00 PM
        Assert.Contains("6:00 PM", csv);
        Assert.DoesNotContain("18:00", csv);
    }

    [Fact]
    public void BuildGameChangerCsv_SeparatesLocationAndField()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new(
                SlotId: "slot-1",
                GameDate: "2026-04-06",
                StartTime: "18:00",
                EndTime: "19:00",
                FieldKey: "park/field1",
                HomeTeamId: "TeamA",
                AwayTeamId: "TeamB",
                IsExternalOffer: false)
        };

        var fieldDetails = new Dictionary<string, FieldDetails>
        {
            ["park/field1"] = new FieldDetails("Oak Park", "Field 1")
        };

        var csv = ScheduleExport.BuildGameChangerCsv(assignments, fieldDetails);

        Assert.Contains("Oak Park", csv);
        Assert.Contains("Field 1", csv);
    }

    [Fact]
    public void BuildGameChangerCsv_AssignsSequentialGameNumbers()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new(
                SlotId: "slot-1",
                GameDate: "2026-04-06",
                StartTime: "18:00",
                EndTime: "19:00",
                FieldKey: "park/field1",
                HomeTeamId: "TeamA",
                AwayTeamId: "TeamB",
                IsExternalOffer: false),
            new(
                SlotId: "slot-2",
                GameDate: "2026-04-07",
                StartTime: "19:00",
                EndTime: "20:00",
                FieldKey: "park/field2",
                HomeTeamId: "TeamC",
                AwayTeamId: "TeamD",
                IsExternalOffer: false)
        };

        var fieldDetails = new Dictionary<string, FieldDetails>
        {
            ["park/field1"] = new FieldDetails("Oak Park", "Field 1"),
            ["park/field2"] = new FieldDetails("Oak Park", "Field 2")
        };

        var csv = ScheduleExport.BuildGameChangerCsv(assignments, fieldDetails);

        var lines = csv.Split('\n');
        // Header + 2 data rows
        Assert.Equal(3, lines.Length);
        // First game should have game number 1
        Assert.Contains("\"1\"", lines[1]);
        // Second game should have game number 2
        Assert.Contains("\"2\"", lines[2]);
    }

    [Fact]
    public void BuildGameChangerCsv_FallsBackToParsingFieldKey()
    {
        var assignments = new List<ScheduleAssignment>
        {
            new(
                SlotId: "slot-1",
                GameDate: "2026-04-06",
                StartTime: "18:00",
                EndTime: "19:00",
                FieldKey: "oakpark/field1",
                HomeTeamId: "TeamA",
                AwayTeamId: "TeamB",
                IsExternalOffer: false)
        };

        // Empty field details - should fall back to parsing field key
        var fieldDetails = new Dictionary<string, FieldDetails>();

        var csv = ScheduleExport.BuildGameChangerCsv(assignments, fieldDetails);

        // Should parse "oakpark/field1" and use as location/field
        Assert.Contains("oakpark", csv);
        Assert.Contains("field1", csv);
    }
}
