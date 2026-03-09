namespace GameSwap.Functions.Storage;

public static class SlotKeyUtil
{
    private static readonly HashSet<char> UnsafeChars = ['/', '\\', '#', '?'];

    public static string BuildAvailabilitySlotId(string gameDate, string startTime, string endTime, string fieldKey)
    {
        var normalizedFieldKey = NormalizeFieldKey(fieldKey);
        return SafeKey($"|{(gameDate ?? "").Trim()}|{(startTime ?? "").Trim()}|{(endTime ?? "").Trim()}|{normalizedFieldKey}");
    }

    public static string BuildIdentity(string division, string fieldKey, string gameDate, string startTime, string endTime)
    {
        return string.Join("|",
            (division ?? "").Trim(),
            NormalizeFieldKey(fieldKey),
            (gameDate ?? "").Trim(),
            (startTime ?? "").Trim(),
            (endTime ?? "").Trim());
    }

    public static string NormalizeFieldKey(string fieldKey)
    {
        if (TrySplitFieldKey(fieldKey, out var parkCode, out var fieldCode))
        {
            return $"{parkCode}/{fieldCode}";
        }

        return (fieldKey ?? "").Trim().Trim('/').ToLowerInvariant();
    }

    public static bool TrySplitFieldKey(string? raw, out string parkCode, out string fieldCode)
    {
        parkCode = "";
        fieldCode = "";
        var value = (raw ?? "").Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        parkCode = Slug.Make(parts[0]);
        fieldCode = Slug.Make(parts[1]);
        return !string.IsNullOrWhiteSpace(parkCode) && !string.IsNullOrWhiteSpace(fieldCode);
    }

    public static string SafeKey(string input)
    {
        var source = input ?? "";
        var chars = new char[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            var value = source[index];
            chars[index] = UnsafeChars.Contains(value) ? '_' : value;
        }

        return new string(chars);
    }
}
