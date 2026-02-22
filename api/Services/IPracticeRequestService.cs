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
        string? reason);

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

    Task<TableEntity> RejectRequestAsync(
        string leagueId,
        string userId,
        string requestId,
        string? reason);
}
