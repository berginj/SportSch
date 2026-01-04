namespace GameSwap.Functions.Functions;

public record ScheduleSlotDto(
    string slotId,
    string gameDate,
    string startTime,
    string endTime,
    string fieldKey,
    string homeTeamId,
    string awayTeamId,
    bool isExternalOffer
);
