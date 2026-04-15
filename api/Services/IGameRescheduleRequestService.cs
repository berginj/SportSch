using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service interface for game reschedule request workflows.
/// </summary>
public interface IGameRescheduleRequestService
{
    /// <summary>
    /// Creates a game reschedule request.
    /// Validates 72-hour lead time and checks conflicts for both teams.
    /// </summary>
    Task<TableEntity> CreateRescheduleRequestAsync(
        string leagueId,
        string userId,
        string division,
        string originalSlotId,
        string proposedSlotId,
        string reason);

    /// <summary>
    /// Opponent team approves the reschedule request.
    /// Transitions to ApprovedByBothTeams and triggers finalization.
    /// </summary>
    Task<TableEntity> OpponentApproveAsync(
        string leagueId,
        string userId,
        string requestId,
        string? response);

    /// <summary>
    /// Opponent team rejects the reschedule request.
    /// </summary>
    Task<TableEntity> OpponentRejectAsync(
        string leagueId,
        string userId,
        string requestId,
        string? response);

    /// <summary>
    /// Finalizes the reschedule (atomic: cancel original + confirm new).
    /// </summary>
    Task<TableEntity> FinalizeAsync(
        string leagueId,
        string userId,
        string requestId);

    /// <summary>
    /// Requesting team cancels the reschedule request.
    /// </summary>
    Task<TableEntity> CancelAsync(
        string leagueId,
        string userId,
        string requestId);

    /// <summary>
    /// Queries reschedule requests filtered by status and user's team involvement.
    /// </summary>
    Task<List<TableEntity>> QueryRequestsAsync(
        string leagueId,
        string userId,
        string? status);

    /// <summary>
    /// Checks for schedule conflicts for both teams at proposed time.
    /// </summary>
    Task<GameRescheduleConflictCheckResponse> CheckConflictsAsync(
        string leagueId,
        string division,
        string originalSlotId,
        string proposedSlotId);
}
