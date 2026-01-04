using System.Text;

namespace GameSwap.Functions.Storage;

public record ScheduleExportRow(
    string EventType,
    string Date,
    string StartTime,
    string EndTime,
    string Duration,
    string HomeTeam,
    string AwayTeam,
    string Venue,
    string Status
);

public static class ScheduleExportCsv
{
    private static readonly string[] Header = new[]
    {
        "Event Type",
        "Date",
        "Start Time",
        "End Time",
        "Duration",
        "Home Team",
        "Away Team",
        "Venue",
        "Status"
    };

    public static string Build(IEnumerable<ScheduleExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Header.Select(Escape)));

        foreach (var row in rows)
        {
            var values = new[]
            {
                row.EventType,
                row.Date,
                row.StartTime,
                row.EndTime,
                row.Duration,
                row.HomeTeam,
                row.AwayTeam,
                row.Venue,
                row.Status
            };
            sb.AppendLine(string.Join(",", values.Select(Escape)));
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string Escape(string? value)
    {
        var safe = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{safe}\"";
    }
}
