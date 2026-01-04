namespace GameSwap.Functions.Scheduling;

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

    public static string BuildInternalCsv(IEnumerable<ScheduleAssignment> assignments, string division)
    {
        var rows = new List<string[]> { InternalHeader };
        rows.AddRange(assignments.Select(a =>
        [
            division ?? "",
            a.GameDate ?? "",
            a.StartTime ?? "",
            a.EndTime ?? "",
            a.FieldKey ?? "",
            a.HomeTeamId ?? "",
            a.AwayTeamId ?? "",
            a.IsExternalOffer ? "true" : "false"
        ]));

        return BuildCsv(rows);
    }

    public static string BuildSportsEngineCsv(IEnumerable<ScheduleAssignment> assignments, IReadOnlyDictionary<string, string> fieldsByKey)
    {
        var rows = new List<string[]> { SportsEngineHeader };
        foreach (var a in assignments)
        {
            var duration = CalcDurationMinutes(a.StartTime, a.EndTime);
            fieldsByKey.TryGetValue(a.FieldKey ?? "", out var venue);
            rows.Add([
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
            ]);
        }

        return BuildCsv(rows);
    }

    private static int? CalcDurationMinutes(string start, string end)
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
