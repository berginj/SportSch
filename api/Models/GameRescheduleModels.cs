namespace GameSwap.Functions.Models;

/// <summary>
/// Request to create a game reschedule request.
/// </summary>
public record GameRescheduleRequestCreateRequest(
    string Division,
    string OriginalSlotId,
    string ProposedSlotId,
    string Reason);

/// <summary>
/// Opponent's decision on a reschedule request.
/// </summary>
public record GameRescheduleOpponentDecisionRequest(
    string? Response);

/// <summary>
/// Admin's override decision.
/// </summary>
public record GameRescheduleAdminOverrideRequest(
    string Action,
    string Reason);

/// <summary>
/// Response containing game reschedule request details.
/// </summary>
public record GameRescheduleRequestResponse(
    string RequestId,
    string LeagueId,
    string Division,
    string Status,
    string RequestingTeamId,
    string OpponentTeamId,
    string OriginalSlotId,
    string ProposedSlotId,
    string Reason,
    string OriginalGameDate,
    string OriginalStartTime,
    string OriginalEndTime,
    string OriginalFieldKey,
    string OriginalFieldName,
    string ProposedGameDate,
    string ProposedStartTime,
    string ProposedEndTime,
    string ProposedFieldKey,
    string ProposedFieldName,
    string RequestingCoachUserId,
    DateTimeOffset RequestedUtc,
    DateTimeOffset? OpponentApprovedUtc,
    string? OpponentApprovedBy,
    string? OpponentResponse,
    DateTimeOffset? AdminReviewedUtc,
    string? AdminReviewedBy,
    string? AdminReviewReason,
    DateTimeOffset? FinalizedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// Conflict check response for game reschedules.
/// </summary>
public record GameRescheduleConflictCheckResponse(
    bool HomeTeamHasConflicts,
    bool AwayTeamHasConflicts,
    List<GameRescheduleConflictDto> HomeTeamConflicts,
    List<GameRescheduleConflictDto> AwayTeamConflicts);

/// <summary>
/// Individual conflict detected during game reschedule check.
/// </summary>
public record GameRescheduleConflictDto(
    string Type,
    string Date,
    string StartTime,
    string EndTime,
    string Location,
    string? Opponent,
    string Status);

/// <summary>
/// Status constants for game reschedule requests.
/// </summary>
public static class GameRescheduleRequestStatuses
{
    public const string PendingOpponent = "PendingOpponent";
    public const string ApprovedByBothTeams = "ApprovedByBothTeams";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Finalized = "Finalized";
    public const string Expired = "Expired"; // Future: auto-expire after 7 days
}
