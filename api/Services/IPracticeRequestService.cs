using Azure.Data.Tables;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service interface for practice request business logic.
/// </summary>
public interface IPracticeRequestService
{
    Task<TableEntity> CreateRequestAsync(
        string leagueId,
        string userId,
        string division,
        string teamId,
        string slotId,
        string? reason,
        bool openToShareField,
        string? shareWithTeamId,
        int? priority = null);

    Task<List<TableEntity>> QueryRequestsAsync(
        string leagueId,
        string userId,
        string? statusFilter,
        string? teamIdFilter);

    Task<TableEntity> ApproveRequestAsync(
        string leagueId,
        string userId,
        string requestId,
        string? reason);

    Task<TableEntity> AutoApproveRequestAsync(
        string leagueId,
        string requestId,
        string reviewedBy,
        string? reason);

    Task<TableEntity> RejectRequestAsync(
        string leagueId,
        string userId,
        string requestId,
        string? reason);

    Task<TableEntity> CancelRequestAsync(
        string leagueId,
        string userId,
        string requestId,
        string? reason = null);

    Task<TableEntity> CreateMoveRequestAsync(
        string leagueId,
        string userId,
        string sourceRequestId,
        string targetSlotId,
        string? reason);
}
