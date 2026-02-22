using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository interface for practice request data access.
/// </summary>
public interface IPracticeRequestRepository
{
    /// <summary>
    /// Creates a new practice request.
    /// </summary>
    Task CreateRequestAsync(TableEntity request);

    /// <summary>
    /// Gets a single practice request by ID.
    /// </summary>
    Task<TableEntity?> GetRequestAsync(string leagueId, string requestId);

    /// <summary>
    /// Updates an existing practice request with optimistic concurrency.
    /// </summary>
    Task UpdateRequestAsync(TableEntity request, ETag etag);

    /// <summary>
    /// Queries practice requests by optional filters.
    /// </summary>
    Task<List<TableEntity>> QueryRequestsAsync(
        string leagueId,
        string? status = null,
        string? division = null,
        string? teamId = null,
        string? slotId = null);

    /// <summary>
    /// Counts requests for a specific team in a division restricted to statuses.
    /// </summary>
    Task<int> CountRequestsForTeamAsync(
        string leagueId,
        string division,
        string teamId,
        IReadOnlyCollection<string> statuses);

    /// <summary>
    /// Checks whether an active request already exists for team + slot.
    /// </summary>
    Task<bool> ExistsRequestForTeamSlotAsync(
        string leagueId,
        string division,
        string teamId,
        string slotId,
        IReadOnlyCollection<string> statuses);

    /// <summary>
    /// Queries all requests for a specific slot, optionally filtered by statuses.
    /// </summary>
    Task<List<TableEntity>> QuerySlotRequestsAsync(
        string leagueId,
        string division,
        string slotId,
        IReadOnlyCollection<string>? statuses = null);
}
