using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Scheduling;

public record AvailabilityRuleSpec(
    string RuleId,
    string FieldKey,
    string Division,
    DateOnly StartsOn,
    DateOnly EndsOn,
    HashSet<DayOfWeek> Days,
    int StartMin,
    int EndMin);

public record AvailabilityExceptionSpec(
    DateOnly DateFrom,
    DateOnly DateTo,
    int StartMin,
    int EndMin);

public record AvailabilitySlotCandidate(
    string GameDate,
    string StartTime,
    string EndTime,
    string FieldKey,
    string Division);

public static class AvailabilityRuleEngine
{
    public static List<AvailabilitySlotCandidate> ExpandRecurringSlots(
        IReadOnlyList<AvailabilityRuleSpec> rules,
        IReadOnlyDictionary<string, List<AvailabilityExceptionSpec>> exceptionsByRule,
        DateOnly from,
        DateOnly to,
        int gameLengthMinutes,
        List<(DateOnly start, DateOnly end)> blackouts)
    {
        var slots = new List<AvailabilitySlotCandidate>();
        if (rules.Count == 0 || gameLengthMinutes <= 0) return slots;

        foreach (var rule in rules)
        {
            var ruleStart = rule.StartsOn < from ? from : rule.StartsOn;
            var ruleEnd = rule.EndsOn > to ? to : rule.EndsOn;
            if (ruleEnd < ruleStart) continue;

            for (var date = ruleStart; date <= ruleEnd; date = date.AddDays(1))
            {
                if (!rule.Days.Contains(date.DayOfWeek)) continue;
                if (IsBlackout(date, blackouts)) continue;

                var start = rule.StartMin;
                while (start + gameLengthMinutes <= rule.EndMin)
                {
                    var end = start + gameLengthMinutes;
                    if (!IsException(exceptionsByRule, rule.RuleId, date, start, end))
                    {
                        slots.Add(new AvailabilitySlotCandidate(
                            date.ToString("yyyy-MM-dd"),
                            FormatTime(start),
                            FormatTime(end),
                            rule.FieldKey,
                            rule.Division
                        ));
                    }
                    start = end;
                }
            }
        }

        return slots;
    }

    private static bool IsException(
        IReadOnlyDictionary<string, List<AvailabilityExceptionSpec>> exceptionsByRule,
        string ruleId,
        DateOnly date,
        int startMin,
        int endMin)
    {
        if (!exceptionsByRule.TryGetValue(ruleId, out var list) || list.Count == 0) return false;
        foreach (var ex in list)
        {
            if (date < ex.DateFrom || date > ex.DateTo) continue;
            if (TimeUtil.Overlaps(startMin, endMin, ex.StartMin, ex.EndMin)) return true;
        }

        return false;
    }

    private static bool IsBlackout(DateOnly date, List<(DateOnly start, DateOnly end)> blackouts)
    {
        foreach (var (start, end) in blackouts)
        {
            if (date >= start && date <= end) return true;
        }
        return false;
    }

    private static string FormatTime(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return $"{h:D2}:{m:D2}";
    }
}
