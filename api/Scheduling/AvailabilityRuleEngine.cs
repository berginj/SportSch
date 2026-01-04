using System.Globalization;

namespace GameSwap.Functions.Scheduling;

public record AvailabilityRule(
    string Division,
    string FieldKey,
    DateOnly StartsOn,
    DateOnly EndsOn,
    IReadOnlyCollection<DayOfWeek> DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string RecurrencePattern = "Weekly");

public record AvailabilityException(DateOnly StartDate, DateOnly EndDate, string Label = "");

public record AvailabilitySlot(
    DateOnly GameDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string FieldKey,
    string Division);

public static class AvailabilityRuleEngine
{
    public static List<AvailabilitySlot> ExpandSlots(
        AvailabilityRule rule,
        IReadOnlyCollection<AvailabilityException> exceptions,
        int gameLengthMinutes)
    {
        if (gameLengthMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(gameLengthMinutes), "Game length must be positive.");

        var slots = new List<AvailabilitySlot>();
        var startMinutes = ToMinutes(rule.StartTime);
        var endMinutes = ToMinutes(rule.EndTime);
        if (endMinutes <= startMinutes)
            return slots;

        for (var date = rule.StartsOn; date <= rule.EndsOn; date = date.AddDays(1))
        {
            if (rule.RecurrencePattern.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
            {
                if (!rule.DaysOfWeek.Contains(date.DayOfWeek)) continue;
            }

            if (IsException(date, exceptions)) continue;

            var cursor = startMinutes;
            while (cursor + gameLengthMinutes <= endMinutes)
            {
                var start = FromMinutes(cursor);
                var end = FromMinutes(cursor + gameLengthMinutes);
                slots.Add(new AvailabilitySlot(date, start, end, rule.FieldKey, rule.Division));
                cursor += gameLengthMinutes;
            }
        }

        return slots;
    }

    public static string FormatDate(DateOnly date)
        => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatTime(TimeOnly time)
        => $"{time.Hour:D2}:{time.Minute:D2}";

    private static bool IsException(DateOnly date, IReadOnlyCollection<AvailabilityException> exceptions)
    {
        foreach (var ex in exceptions)
        {
            if (date >= ex.StartDate && date <= ex.EndDate) return true;
        }

        return false;
    }

    private static int ToMinutes(TimeOnly time)
        => time.Hour * 60 + time.Minute;

    private static TimeOnly FromMinutes(int minutes)
        => new(minutes / 60, minutes % 60);
}
