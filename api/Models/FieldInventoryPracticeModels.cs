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
    string? Notes);

public record FieldInventoryPracticeRequestDecisionRequest(string? Reason);

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
    int RequestableBlockCount,
    int ApprovedTeamCount,
    int PendingTeamCount,
    List<string> MappingIssues);

public record FieldInventoryPracticeSlotDto(
    string PracticeSlotKey,
    string SeasonLabel,
    string LiveRecordId,
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
    string? AssignedGroup,
    string? AssignedDivision,
    string? AssignedTeamOrEvent,
    int Capacity,
    int ApprovedCount,
    int PendingCount,
    int RemainingCapacity,
    List<string> ApprovedTeamIds,
    List<string> PendingTeamIds);

public record FieldInventoryPracticeRequestDto(
    string RequestId,
    string SeasonLabel,
    string PracticeSlotKey,
    string LiveRecordId,
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
    string? Notes,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewReason);

public record FieldInventoryPracticeAdminResponse(
    string SeasonLabel,
    List<FieldInventoryPracticeSeasonOptionDto> Seasons,
    FieldInventoryPracticeSummaryDto Summary,
    List<FieldInventoryPracticeAdminRowDto> Rows,
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

public class FieldInventoryPracticeRequestEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string SeasonLabel { get; set; } = "";
    public string PracticeSlotKey { get; set; } = "";
    public string LiveRecordId { get; set; } = "";
    public string Date { get; set; } = "";
    public string DayOfWeek { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public string FieldId { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string? TeamName { get; set; }
    public string BookingPolicy { get; set; } = FieldInventoryPracticeBookingPolicies.CommissionerReview;
    public string Status { get; set; } = FieldInventoryPracticeRequestStatuses.Pending;
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
