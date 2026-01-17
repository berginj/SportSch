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
    private static readonly string[] InternalHeader = new[]
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

    private static readonly string[] SportsEngineHeader = new[]
    {
        "Event Type",
        "Event Name (Events Only)",
        "Description (Events Only)",
        "Date",
        "Start Time",
        "End Time",
        "Duration (minutes)",
        "All Day Event (Events Only)",
        "Home Team",
        "Away Team",
        "Teams (Events Only)",
        "Venue",
        "Status"
    };

    private static readonly string[] GameChangerHeader = new[]
    {
        "Date",
        "Time",
        "Home Team",
        "Away Team",
        "Location",
        "Field",
        "Game Type",
        "Game Number"
    };

    public static string Build(IEnumerable<ScheduleExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", InternalHeader.Select(Escape)));

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

    public static string BuildSportsEngine(IEnumerable<ScheduleExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", SportsEngineHeader.Select(Escape)));

        foreach (var row in rows)
        {
            var values = new[]
            {
                row.EventType,
                "", // Event Name (Events Only)
                "", // Description (Events Only)
                row.Date,
                row.StartTime,
                row.EndTime,
                row.Duration,
                "", // All Day Event (Events Only)
                row.HomeTeam,
                row.AwayTeam,
                "", // Teams (Events Only)
                row.Venue,
                row.Status
            };
            sb.AppendLine(string.Join(",", values.Select(Escape)));
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static string BuildGameChanger(IEnumerable<ScheduleExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", GameChangerHeader.Select(Escape)));

        var gameNumber = 1;
        foreach (var row in rows)
        {
            var date = FormatDateForGameChanger(row.Date);
            var time = FormatTimeForGameChanger(row.StartTime);

            // Parse venue into location and field
            // Venue format is typically "Park Name > Field Name" or "Park Name Field Name"
            var (location, field) = ParseVenueForGameChanger(row.Venue);

            var values = new[]
            {
                date,
                time,
                row.HomeTeam,
                row.AwayTeam,
                location,
                field,
                "Regular Season",
                gameNumber.ToString()
            };
            sb.AppendLine(string.Join(",", values.Select(Escape)));
            gameNumber++;
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string FormatDateForGameChanger(string? isoDate)
    {
        // Convert "2026-04-06" to "04/06/2026"
        if (string.IsNullOrWhiteSpace(isoDate)) return "";

        var parts = isoDate.Split('-');
        if (parts.Length != 3) return isoDate;

        if (!int.TryParse(parts[0], out var year)) return isoDate;
        if (!int.TryParse(parts[1], out var month)) return isoDate;
        if (!int.TryParse(parts[2], out var day)) return isoDate;

        return $"{month:D2}/{day:D2}/{year:D4}";
    }

    private static string FormatTimeForGameChanger(string? time24)
    {
        // Convert "18:00" to "6:00 PM"
        if (string.IsNullOrWhiteSpace(time24)) return "";

        var parts = time24.Split(':');
        if (parts.Length < 2) return time24;

        if (!int.TryParse(parts[0], out var hour)) return time24;
        if (!int.TryParse(parts[1], out var minute)) return time24;

        var period = hour >= 12 ? "PM" : "AM";
        var hour12 = hour == 0 ? 12 : (hour > 12 ? hour - 12 : hour);

        return $"{hour12}:{minute:D2} {period}";
    }

    private static (string location, string field) ParseVenueForGameChanger(string? venue)
    {
        if (string.IsNullOrWhiteSpace(venue))
            return ("", "");

        // Try to split on common delimiters
        if (venue.Contains(" > "))
        {
            var parts = venue.Split(new[] { " > " }, StringSplitOptions.None);
            return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
        }

        if (venue.Contains("/"))
        {
            var parts = venue.Split('/');
            return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
        }

        // If no delimiter, try to extract field number from end
        var match = System.Text.RegularExpressions.Regex.Match(venue, @"^(.+?)\s+(Field\s*\d+|Diamond\s*\d+|\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
        }

        // Fallback: use entire venue as location
        return (venue, "");
    }

    private static string Escape(string? value)
    {
        var safe = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{safe}\"";
    }
}
