namespace GameSwap.Functions.Storage;

/// <summary>
/// Simple helpers for working with HH:MM (24-hour) strings.
/// Times are interpreted as US/Eastern per /docs/contract.md; the API stores/returns strings (no conversion).
/// </summary>
public static class TimeUtil
{
    public static bool TryParseMinutes(string hhmm, out int minutes)
    {
        minutes = 0;
        var s = (hhmm ?? "").Trim();
        var parts = s.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], out var h)) return false;
        if (!int.TryParse(parts[1], out var m)) return false;

        if (h < 0 || h > 23) return false;
        if (m < 0 || m > 59) return false;

        minutes = (h * 60) + m;
        return true;
    }

    public static bool IsValidRange(string startTime, string endTime, out int startMinutes, out int endMinutes, out string error)
    {
        startMinutes = 0;
        endMinutes = 0;
        error = "";

        if (!TryParseMinutes(startTime, out startMinutes) || !TryParseMinutes(endTime, out endMinutes))
        {
            error = "Invalid time format. Expected HH:MM (24-hour).";
            return false;
        }

        if (startMinutes >= endMinutes)
        {
            error = "startTime must be before endTime.";
            return false;
        }

        return true;
    }

    public static bool Overlaps(int startA, int endA, int startB, int endB)
        => startA < endB && endA > startB;
}
