namespace GameSwap.Functions.Models;

public static class FieldInventoryPracticeBookingPolicies
{
    public const string AutoApprove = "auto_approve";
    public const string CommissionerReview = "commissioner_review";
    public const string NotRequestable = "not_requestable";
}

public static class FieldInventoryPracticeRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

public record FieldInventoryDivisionAliasSaveRequest(
    string? RawDivisionName,
    string? CanonicalDivisionCode);

public record FieldInventoryTeamAliasSaveRequest(
    string? RawTeamName,
    string? CanonicalDivisionCode,
    string? CanonicalTeamId,
    string? CanonicalTeamName);

public record FieldInventoryGroupPolicySaveRequest(
    string? RawGroupName,
    string? BookingPolicy);

public record FieldInventoryPracticeRequestCreateRequest(
    string? SeasonLabel,
    string? PracticeSlotKey,
    string? TeamId,
    string? Notes,
    bool OpenToShareField = false,
    string? ShareWithTeamId = null);

public record FieldInventoryPracticeRequestMoveRequest(
    string? SeasonLabel,
    string? PracticeSlotKey,
    string? Notes,
    bool OpenToShareField = false,
    string? ShareWithTeamId = null);

public record FieldInventoryPracticeRequestDecisionRequest(string? Reason);

public record FieldInventoryPracticeNormalizeRequest(
    string? SeasonLabel,
    string? DateFrom,
    string? DateTo,
    string? FieldId,
    bool DryRun);

public record FieldInventoryPracticeSummaryDto(
    int TotalRecords,
    int RequestableBlocks,
    int AutoApproveBlocks,
    int CommissionerReviewBlocks,
    int PendingRequests,
    int ApprovedRequests,
    int UnmappedDivisions,
    int UnmappedTeams,
    int UnmappedPolicies);

public record FieldInventoryPracticeSeasonOptionDto(string SeasonLabel, bool IsDefault);

public record FieldInventoryPracticeAdminRowDto(
    string RecordId,
    string SeasonLabel,
    string Date,
    string DayOfWeek,
    string StartTime,
    string EndTime,
    int SlotDurationMinutes,
    string AvailabilityStatus,
    string UtilizationStatus,
    string? UsageType,
    string? UsedBy,
    string FieldId,
    string FieldName,
    string RawFieldName,
    string? AssignedGroup,
    string? RawAssignedDivision,
    string? RawAssignedTeamOrEvent,
    string? CanonicalDivisionCode,
    string? CanonicalDivisionName,
    string? CanonicalTeamId,
    string? CanonicalTeamName,
    string BookingPolicy,
    string BookingPolicyReason,
    List<string> MappingIssues);

public record FieldInventoryPracticeSlotDto(
    string PracticeSlotKey,
    string SeasonLabel,
    string LiveRecordId,
    string SlotId,
    string Division,
    string Date,
    string DayOfWeek,
    string StartTime,
    string EndTime,
    int SlotDurationMinutes,
    string FieldId,
    string FieldName,
    string BookingPolicy,
    string BookingPolicyLabel,
    string BookingPolicyReason,
    string NormalizationState,
    List<string> NormalizationIssues,
    string? AssignedGroup,
    string? AssignedDivision,
    string? AssignedTeamOrEvent,
    bool IsAvailable,
    List<string> PendingTeamIds,
    bool Shareable,
    int MaxTeamsPerBooking,
    List<string> ReservedTeamIds,
    List<string> PendingShareTeamIds);

public record FieldInventoryPracticeRequestDto(
    string RequestId,
    string SeasonLabel,
    string PracticeSlotKey,
    string LiveRecordId,
    string SlotId,
    string Division,
    string Date,
    string DayOfWeek,
    string StartTime,
    string EndTime,
    string FieldId,
    string FieldName,
    string TeamId,
    string? TeamName,
    string Status,
    string BookingPolicy,
    string BookingPolicyLabel,
    bool IsMove,
    string? MoveFromRequestId,
    string? MoveFromDate,
    string? MoveFromStartTime,
    string? MoveFromEndTime,
    string? MoveFromFieldName,
    string? Notes,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewReason,
    bool OpenToShareField,
    string? ShareWithTeamId,
    List<string> ReservedTeamIds);

public record FieldInventoryPracticeNormalizationSummaryDto(
    int CandidateBlocks,
    int NormalizedBlocks,
    int MissingBlocks,
    int ConflictBlocks,
    int BlockedBlocks);

public record FieldInventoryPracticeAdminResponse(
    string SeasonLabel,
    List<FieldInventoryPracticeSeasonOptionDto> Seasons,
    FieldInventoryPracticeSummaryDto Summary,
    FieldInventoryPracticeNormalizationSummaryDto Normalization,
    List<FieldInventoryPracticeAdminRowDto> Rows,
    List<FieldInventoryPracticeSlotDto> Slots,
    List<FieldInventoryPracticeRequestDto> Requests,
    List<CanonicalFieldOptionDto> CanonicalFields,
    List<CanonicalDivisionOptionDto> CanonicalDivisions,
    List<CanonicalTeamOptionDto> CanonicalTeams);

public record FieldInventoryPracticeCoachResponse(
    string SeasonLabel,
    List<FieldInventoryPracticeSeasonOptionDto> Seasons,
    string Division,
    string TeamId,
    string? TeamName,
    FieldInventoryPracticeSummaryDto Summary,
    List<FieldInventoryPracticeSlotDto> Slots,
    List<FieldInventoryPracticeRequestDto> Requests);

public record FieldInventoryPracticeNormalizeResultDto(
    int CandidateBlocks,
    int CreatedBlocks,
    int UpdatedBlocks,
    int AlreadyNormalizedBlocks,
    int ConflictBlocks,
    int BlockedBlocks);

public record FieldInventoryPracticeNormalizeResponse(
    FieldInventoryPracticeNormalizeResultDto Result,
    FieldInventoryPracticeAdminResponse AdminView);

public record CanonicalDivisionOptionDto(string Code, string Name);

public record CanonicalTeamOptionDto(string DivisionCode, string TeamId, string TeamName);

public class FieldInventoryDivisionAliasEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string RawDivisionName { get; set; } = "";
    public string NormalizedLookupKey { get; set; } = "";
    public string CanonicalDivisionCode { get; set; } = "";
    public string CanonicalDivisionName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public class FieldInventoryTeamAliasEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string RawTeamName { get; set; } = "";
    public string NormalizedLookupKey { get; set; } = "";
    public string CanonicalDivisionCode { get; set; } = "";
    public string CanonicalTeamId { get; set; } = "";
    public string CanonicalTeamName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public class FieldInventoryGroupPolicyEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string RawGroupName { get; set; } = "";
    public string NormalizedLookupKey { get; set; } = "";
    public string BookingPolicy { get; set; } = FieldInventoryPracticeBookingPolicies.NotRequestable;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public record PracticeConflictCheckRequest(
    string SeasonLabel,
    string PracticeSlotKey);

public record PracticeConflictCheckResponse(
    bool HasConflicts,
    List<PracticeConflictDto> Conflicts);

public record PracticeConflictDto(
    string Type,
    string Date,
    string StartTime,
    string EndTime,
    string Location,
    string? Opponent,
    string Status);

