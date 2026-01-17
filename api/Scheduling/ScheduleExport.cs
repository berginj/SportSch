namespace GameSwap.Functions.Scheduling;

public record FieldDetails(string ParkName, string FieldName);

public static class ScheduleExport
{
    private static readonly string[] InternalHeader =
    [
        "division",
        "gameDate",
        "startTime",
        "endTime",
        "fieldKey",
        "homeTeamId",
        "awayTeamId",
        "isExternalOffer"
    ];

    private static readonly string[] SportsEngineHeader =
    [
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
    ];

    private static readonly string[] GameChangerHeader =
    [
        "Date",
        "Time",
        "Home Team",
        "Away Team",
        "Location",
        "Field",
        "Game Type",
        "Game Number"
    ];

    public static string BuildInternalCsv(IEnumerable<ScheduleAssignment> assignments, string division)
    {
        var rows = new List<string[]> { InternalHeader };
        rows.AddRange(assignments.Select(a => new[]
        {
            division ?? "",
            a.GameDate ?? "",
            a.StartTime ?? "",
            a.EndTime ?? "",
            a.FieldKey ?? "",
            a.HomeTeamId ?? "",
            a.AwayTeamId ?? "",
            a.IsExternalOffer ? "true" : "false"
        }));

        return BuildCsv(rows);
    }

    public static string BuildSportsEngineCsv(IEnumerable<ScheduleAssignment> assignments, IReadOnlyDictionary<string, string> fieldsByKey)
    {
        var rows = new List<string[]> { SportsEngineHeader };
        foreach (var a in assignments)
        {
            var duration = CalcDurationMinutes(a.StartTime, a.EndTime);
            fieldsByKey.TryGetValue(a.FieldKey ?? "", out var venue);
            rows.Add(new[]
            {
                "Game",
                "",
                "",
                a.GameDate ?? "",
                a.StartTime ?? "",
                a.EndTime ?? "",
                duration?.ToString() ?? "",
                "",
                a.HomeTeamId ?? "",
                a.AwayTeamId ?? "",
                "",
                venue ?? a.FieldKey ?? "",
                "Scheduled"
            });
        }

        return BuildCsv(rows);
    }

    public static string BuildGameChangerCsv(IEnumerable<ScheduleAssignment> assignments, IReadOnlyDictionary<string, FieldDetails> fieldDetailsByKey)
    {
        var rows = new List<string[]> { GameChangerHeader };
        var gameNumber = 1;

        foreach (var a in assignments)
        {
            var date = FormatDateForGameChanger(a.GameDate);
            var time = FormatTimeForGameChanger(a.StartTime);

            // Try to get field details, fallback to parsing field key
            var location = "";
            var field = "";
            if (fieldDetailsByKey.TryGetValue(a.FieldKey ?? "", out var details))
            {
                location = details.ParkName ?? "";
                field = details.FieldName ?? "";
            }
            else
            {
                // Fallback: parse field key (format: "parkCode/fieldCode")
                var parts = (a.FieldKey ?? "").Split('/');
                location = parts.Length > 0 ? parts[0] : "";
                field = parts.Length > 1 ? parts[1] : "";
            }

            rows.Add(new[]
            {
                date,
                time,
                a.HomeTeamId ?? "",
                a.AwayTeamId ?? "",
                location,
                field,
                "Regular Season",
                gameNumber.ToString()
            });

            gameNumber++;
        }

        return BuildCsv(rows);
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

    private static int? CalcDurationMinutes(string? start, string? end)
    {
        var s = ParseMinutes(start);
        var e = ParseMinutes(end);
        if (!s.HasValue || !e.HasValue || e.Value <= s.Value) return null;
        return e.Value - s.Value;
    }

    private static int? ParseMinutes(string value)
    {
        var parts = (value ?? "").Split(':');
        if (parts.Length < 2) return null;
        if (!int.TryParse(parts[0], out var h)) return null;
        if (!int.TryParse(parts[1], out var m)) return null;
        return h * 60 + m;
    }

    private static string BuildCsv(IEnumerable<string[]> rows)
    {
        return string.Join("\n", rows.Select(row => string.Join(",", row.Select(Escape))));
    }

    private static string Escape(string? value)
    {
        var text = value ?? "";
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
