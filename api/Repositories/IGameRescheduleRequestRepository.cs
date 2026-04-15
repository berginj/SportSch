using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository interface for game reschedule request data access.
/// </summary>
public interface IGameRescheduleRequestRepository
{
    /// <summary>
    /// Creates a new game reschedule request.
    /// </summary>
    Task CreateRequestAsync(TableEntity request);

    /// <summary>
    /// Gets a single game reschedule request by ID.
    /// </summary>
    Task<TableEntity?> GetRequestAsync(string leagueId, string requestId);

    /// <summary>
    /// Updates an existing game reschedule request with optimistic concurrency.
    /// </summary>
    Task UpdateRequestAsync(TableEntity request, ETag etag);

    /// <summary>
    /// Queries game reschedule requests by optional filters.
    /// Filters by team involvement (requesting OR opponent team).
    /// </summary>
    Task<List<TableEntity>> QueryRequestsAsync(
        string leagueId,
        string? status = null,
        string? teamId = null);

    /// <summary>
    /// Queries all reschedule requests for a specific game slot.
    /// </summary>
    Task<List<TableEntity>> QueryRequestsBySlotAsync(
        string leagueId,
        string division,
        string slotId);

    /// <summary>
    /// Checks if an active reschedule request already exists for a slot.
    /// </summary>
    Task<bool> HasActiveRequestForSlotAsync(
        string leagueId,
        string division,
        string slotId,
        IReadOnlyCollection<string> activeStatuses);
}
