using System.Globalization;

namespace GameSwap.Functions.Storage;

public static class ScheduleTime
{
    public static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static bool TryGetUtc(string? date, string? time, out DateTimeOffset instant)
    {
        instant = default;
        if (!DateTime.TryParseExact($"{date} {time}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local)) return false;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // An ambiguous/nonexistent local time cannot identify a booking instant.
        if (Eastern.IsInvalidTime(local) || Eastern.IsAmbiguousTime(local)) return false;
        instant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Eastern));
        return true;
    }

    public static void RequireLeadTime(string? date, string? time, int hours, DateTimeOffset now)
    {
        if (!TryGetUtc(date, time, out var instant))
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME_RANGE, "A valid, unambiguous Eastern date and time is required.");
        if (instant - now < TimeSpan.FromHours(hours))
            throw new ApiGuards.HttpError(409, ErrorCodes.LEAD_TIME_VIOLATION,
                $"Changes require at least {hours} hours before the scheduled Eastern time. Past bookings cannot be moved.");
    }
}
