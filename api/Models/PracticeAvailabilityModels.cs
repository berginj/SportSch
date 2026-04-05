namespace GameSwap.Functions.Models;

public record PracticeAvailabilityQueryRequest(
    string? SeasonLabel,
    string? Date,
    string? StartTime,
    string? EndTime,
    string? Division,
    string? FieldKey);

public record PracticeAvailabilityOptionDto(
    string SlotId,
    string PracticeSlotKey,
    string SeasonLabel,
    string Division,
    string Date,
    string DayOfWeek,
    string StartTime,
    string EndTime,
    string FieldKey,
    string FieldName,
    string BookingPolicy,
    string BookingPolicyLabel,
    bool IsAvailable,
    bool Shareable,
    int MaxTeamsPerBooking,
    List<string> ReservedTeamIds,
    List<string> PendingTeamIds,
    List<string> PendingShareTeamIds);

public record PracticeAvailabilityOptionsResponse(
    string SeasonLabel,
    string Division,
    string TeamId,
    string? TeamName,
    string? Date,
    string? StartTime,
    string? EndTime,
    string? FieldKey,
    bool ExactMatchRequested,
    int Count,
    List<PracticeAvailabilityOptionDto> Options);

public record PracticeAvailabilityCheckResponse(
    string SeasonLabel,
    string Division,
    string TeamId,
    string? TeamName,
    string Date,
    string StartTime,
    string EndTime,
    string? FieldKey,
    bool Available,
    int MatchingOptionCount,
    List<PracticeAvailabilityOptionDto> Options);
