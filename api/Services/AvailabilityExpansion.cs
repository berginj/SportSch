using System.Linq;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

public static class AvailabilityExpansion
{
    public record AvailabilityRule(
        string RuleId,
        IReadOnlyCollection<DayOfWeek> DaysOfWeek,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string Timezone,
        IReadOnlyCollection<AvailabilityException> Exceptions
    );

    public record AvailabilityException(
        DateOnly Date,
        bool IsAvailable,
        TimeOnly? StartTime,
        TimeOnly? EndTime
    );

    public record AvailabilityOccurrence(
        string RuleId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string Timezone
    );

    public record AvailabilitySlot(
        string RuleId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string Timezone
    );

    public static List<AvailabilitySlot> ExpandRules(
        IReadOnlyCollection<AvailabilityRule> rules,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        int gameLengthMinutes)
    {
        if (rules is null || rules.Count == 0 || gameLengthMinutes <= 0)
        {
            return new List<AvailabilitySlot>();
        }

        var occurrences = new List<AvailabilityOccurrence>();
        foreach (var rule in rules)
        {
            occurrences.AddRange(ExpandWeeklyRecurrence(rule, rangeStart, rangeEnd));
        }

        var withExceptions = occurrences
            .GroupBy(o => o.RuleId)
            .SelectMany(group =>
            {
                var rule = rules.First(r => r.RuleId == group.Key);
                return ApplyExceptions(group, rule.Exceptions);
            })
            .ToList();

        return SliceIntoGameSlots(withExceptions, gameLengthMinutes);
    }

    public static List<AvailabilityOccurrence> ExpandWeeklyRecurrence(
        AvailabilityRule rule,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        var occurrences = new List<AvailabilityOccurrence>();
        if (rule.DaysOfWeek is null || rule.DaysOfWeek.Count == 0)
        {
            return occurrences;
        }

        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            if (!rule.DaysOfWeek.Contains(date.DayOfWeek)) continue;
            if (!IsValidRange(rule.StartTime, rule.EndTime)) continue;

            occurrences.Add(new AvailabilityOccurrence(
                rule.RuleId,
                date,
                rule.StartTime,
                rule.EndTime,
                rule.Timezone
            ));
        }

        return occurrences;
    }

    public static List<AvailabilityOccurrence> ApplyExceptions(
        IEnumerable<AvailabilityOccurrence> occurrences,
        IReadOnlyCollection<AvailabilityException> exceptions)
    {
        if (exceptions is null || exceptions.Count == 0)
        {
            return occurrences.ToList();
        }

        var byDate = exceptions
            .GroupBy(e => e.Date)
            .ToDictionary(g => g.Key, g => g.Last());

        var filtered = new List<AvailabilityOccurrence>();
        foreach (var occurrence in occurrences)
        {
            if (!byDate.TryGetValue(occurrence.Date, out var exception))
            {
                filtered.Add(occurrence);
                continue;
            }

            if (!exception.IsAvailable)
            {
                continue;
            }

            var startTime = exception.StartTime ?? occurrence.StartTime;
            var endTime = exception.EndTime ?? occurrence.EndTime;
            if (!IsValidRange(startTime, endTime))
            {
                continue;
            }

            filtered.Add(new AvailabilityOccurrence(
                occurrence.RuleId,
                occurrence.Date,
                startTime,
                endTime,
                occurrence.Timezone
            ));
        }

        return filtered;
    }

    public static List<AvailabilitySlot> SliceIntoGameSlots(
        IEnumerable<AvailabilityOccurrence> occurrences,
        int gameLengthMinutes)
    {
        var slots = new List<AvailabilitySlot>();
        if (gameLengthMinutes <= 0)
        {
            return slots;
        }

        foreach (var occurrence in occurrences)
        {
            if (!TryGetMinutes(occurrence.StartTime, out var startMin) ||
                !TryGetMinutes(occurrence.EndTime, out var endMin))
            {
                continue;
            }

            var cursor = startMin;
            while (cursor + gameLengthMinutes <= endMin)
            {
                var slotStart = FromMinutes(cursor);
                var slotEnd = FromMinutes(cursor + gameLengthMinutes);
                slots.Add(new AvailabilitySlot(
                    occurrence.RuleId,
                    occurrence.Date,
                    slotStart,
                    slotEnd,
                    occurrence.Timezone
                ));
                cursor += gameLengthMinutes;
            }
        }

        return slots;
    }

    private static bool IsValidRange(TimeOnly startTime, TimeOnly endTime)
    {
        var start = startTime.ToString("HH:mm");
        var end = endTime.ToString("HH:mm");
        return TimeUtil.IsValidRange(start, end, out _, out _, out _);
    }

    private static bool TryGetMinutes(TimeOnly time, out int minutes)
        => TimeUtil.TryParseMinutes(time.ToString("HH:mm"), out minutes);

    private static TimeOnly FromMinutes(int minutes)
        => new(minutes / 60, minutes % 60);
}
