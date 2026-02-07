using GameSwap.Functions.Storage;
using Xunit;

namespace GameSwap.Functions.Tests;

public class ScheduleExportCsvTests
{
    [Fact]
    public void Build_MatchesFixture()
    {
        var rows = new List<ScheduleExportRow>
        {
            new(
                EventType: "Game",
                Date: "2025-05-10",
                StartTime: "09:00",
                EndTime: "10:30",
                Duration: "90",
                HomeTeam: "Falcons",
                AwayTeam: "Hawks",
                Venue: "Central Park > Field 1",
                Status: "Scheduled"
            ),
            new(
                EventType: "Game",
                Date: "2025-05-11",
                StartTime: "18:00",
                EndTime: "19:00",
                Duration: "60",
                HomeTeam: "Lions",
                AwayTeam: "",
                Venue: "West Side > Field B",
                Status: "Open"
            ),
        };

        var csv = ScheduleExportCsv.Build(rows);
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "schedule_export_fixture.csv");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "schedule_export_fixture.csv");
        }
        var fixture = File.ReadAllText(fixturePath).TrimEnd('\r', '\n');

        Assert.Equal(fixture, csv);
    }
}
