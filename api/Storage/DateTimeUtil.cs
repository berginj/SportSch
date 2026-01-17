namespace GameSwap.Functions.Storage;

/// <summary>
/// Extended date/time utilities with timezone support.
/// Complements the existing TimeUtil class.
/// </summary>
public static class DateTimeUtil
{
    /// <summary>
    /// Default timezone for the application (US/Eastern per contract.md).
    /// </summary>
    public const string DefaultTimezone = "America/New_York";

    /// <summary>
    /// Validates that a date string is in strict YYYY-MM-DD format.
    /// </summary>
    public static bool IsValidDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return false;
        return DateOnly.TryParseExact(date, "yyyy-MM-dd", out _);
    }

    /// <summary>
    /// Validates that a time string is in HH:MM format.
    /// </summary>
    public static bool IsValidTime(string? time)
    {
        return TimeUtil.TryParseMinutes(time, out _);
    }

    /// <summary>
    /// Validates a date range (fromDate must be <= toDate).
    /// </summary>
    public static bool IsValidDateRange(string? fromDate, string? toDate, out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(fromDate) || string.IsNullOrWhiteSpace(toDate))
            return true; // Allow missing dates for open-ended ranges

        if (!IsValidDate(fromDate))
        {
            error = "fromDate must be in YYYY-MM-DD format";
            return false;
        }

        if (!IsValidDate(toDate))
        {
            error = "toDate must be in YYYY-MM-DD format";
            return false;
        }

        if (DateOnly.Parse(fromDate) > DateOnly.Parse(toDate))
        {
            error = "fromDate must be less than or equal to toDate";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates a time range (startTime must be < endTime).
    /// </summary>
    public static bool IsValidTimeRange(string? startTime, string? endTime, out string error)
    {
        return TimeUtil.IsValidRange(startTime ?? "", endTime ?? "", out _, out _, out error);
    }

    /// <summary>
    /// Gets the current date in YYYY-MM-DD format (UTC).
    /// </summary>
    public static string TodayUtc()
    {
        return DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Parses a date string to DateOnly.
    /// </summary>
    public static DateOnly? ParseDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        if (DateOnly.TryParseExact(date, "yyyy-MM-dd", out var result))
            return result;
        return null;
    }
}
