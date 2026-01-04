namespace GameSwap.Functions.Scheduling;

public record ScheduleSeedInput(
    string Division,
    string FieldKey,
    IReadOnlyList<string> Teams,
    AvailabilityRule Rule,
    IReadOnlyList<AvailabilityException> Exceptions,
    int GameLengthMinutes,
    ScheduleConstraints Constraints,
    IReadOnlyDictionary<string, string> FieldDisplayNames);

public record ScheduleSeedResult(
    IReadOnlyList<AvailabilitySlot> Slots,
    ScheduleResult Schedule,
    ScheduleValidationResult Validation,
    string InternalCsv,
    string SportsEngineCsv);

public static class ScheduleSeedFlow
{
    public static ScheduleSeedResult Run(ScheduleSeedInput input)
    {
        var slots = AvailabilityRuleEngine.ExpandSlots(input.Rule, input.Exceptions, input.GameLengthMinutes);
        var scheduleSlots = slots
            .Select((slot, index) => new ScheduleSlot(
                SlotId: $"seed-{index + 1}",
                GameDate: AvailabilityRuleEngine.FormatDate(slot.GameDate),
                StartTime: AvailabilityRuleEngine.FormatTime(slot.StartTime),
                EndTime: AvailabilityRuleEngine.FormatTime(slot.EndTime),
                FieldKey: slot.FieldKey,
                OfferingTeamId: ""))
            .ToList();

        var matchups = ScheduleEngine.BuildRoundRobin(input.Teams);
        var schedule = ScheduleEngine.AssignMatchups(scheduleSlots, matchups, input.Teams, input.Constraints);
        var validation = ScheduleValidation.Validate(schedule, input.Constraints);
        var internalCsv = ScheduleExport.BuildInternalCsv(schedule.Assignments, input.Division);
        var sportsEngineCsv = ScheduleExport.BuildSportsEngineCsv(schedule.Assignments, input.FieldDisplayNames);

        return new ScheduleSeedResult(slots, schedule, validation, internalCsv, sportsEngineCsv);
    }
}
