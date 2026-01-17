using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for slot request data access operations.
/// </summary>
public interface IRequestRepository
{
    /// <summary>
    /// Gets a single slot request by ID.
    /// </summary>
    Task<TableEntity?> GetRequestAsync(string leagueId, string division, string slotId, string requestId);

    /// <summary>
    /// Queries requests with optional filtering and pagination.
    /// </summary>
    Task<PaginationResult<TableEntity>> QueryRequestsAsync(RequestQueryFilter filter, string? continuationToken = null);

    /// <summary>
    /// Gets all pending requests for a specific slot.
    /// </summary>
    Task<List<TableEntity>> GetPendingRequestsForSlotAsync(string leagueId, string division, string slotId);

    /// <summary>
    /// Creates a new slot request.
    /// </summary>
    Task CreateRequestAsync(TableEntity request);

    /// <summary>
    /// Updates an existing request (e.g., approval/rejection) with optimistic concurrency.
    /// </summary>
    Task UpdateRequestAsync(TableEntity request, ETag etag);

    /// <summary>
    /// Deletes a request.
    /// </summary>
    Task DeleteRequestAsync(string leagueId, string division, string slotId, string requestId);
}

/// <summary>
/// Filter parameters for querying slot requests.
/// </summary>
public class RequestQueryFilter
{
    public string LeagueId { get; set; } = "";
    public string? Division { get; set; }
    public string? SlotId { get; set; }
    public string? Status { get; set; }
    public int PageSize { get; set; } = 50;
}
