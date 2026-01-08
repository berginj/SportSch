namespace GameSwap.Functions.Storage;

public static class SlotOverlap
{
    public static string BuildRangeKey(string fieldKey, string gameDate)
        => $"{fieldKey}|{gameDate}";

    public static string BuildRangeKey(string fieldKey, DateOnly gameDate)
        => $"{fieldKey}|{gameDate:yyyy-MM-dd}";

    public static int ParseMinutes(string value)
    {
        var parts = (value ?? "").Split(':');
        if (parts.Length < 2) return -1;
        if (!int.TryParse(parts[0], out var h)) return -1;
        if (!int.TryParse(parts[1], out var m)) return -1;
        return h * 60 + m;
    }

    public static bool TryParseMinutesRange(string startTime, string endTime, out int startMin, out int endMin)
    {
        startMin = ParseMinutes(startTime);
        endMin = ParseMinutes(endTime);
        return startMin >= 0 && endMin > startMin;
    }

    public static bool HasOverlap(Dictionary<string, List<(int startMin, int endMin)>> ranges, string key, int startMin, int endMin)
    {
        if (!ranges.TryGetValue(key, out var list)) return false;
        return list.Any(r => r.startMin < endMin && startMin < r.endMin);
    }

    public static bool AddRange(Dictionary<string, List<(int startMin, int endMin)>> ranges, string key, int startMin, int endMin)
    {
        if (!ranges.TryGetValue(key, out var list))
        {
            list = new List<(int startMin, int endMin)>();
            ranges[key] = list;
        }

        if (list.Any(r => r.startMin < endMin && startMin < r.endMin))
            return false;

        list.Add((startMin, endMin));
        return true;
    }
}
