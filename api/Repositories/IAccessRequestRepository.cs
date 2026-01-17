using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository interface for access request data access.
/// </summary>
public interface IAccessRequestRepository
{
    /// <summary>
    /// Gets an access request by leagueId and userId.
    /// </summary>
    Task<TableEntity?> GetAccessRequestAsync(string leagueId, string userId);

    /// <summary>
    /// Queries access requests by userId across all leagues.
    /// </summary>
    Task<List<TableEntity>> QueryAccessRequestsByUserIdAsync(string userId);

    /// <summary>
    /// Queries access requests for a specific league with optional status filter.
    /// </summary>
    Task<List<TableEntity>> QueryAccessRequestsByLeagueAsync(string leagueId, string? status = null);

    /// <summary>
    /// Queries all access requests across all leagues with optional status filter (for global admin).
    /// </summary>
    Task<List<TableEntity>> QueryAllAccessRequestsAsync(string? status = null);

    /// <summary>
    /// Upserts an access request (creates or updates).
    /// </summary>
    Task UpsertAccessRequestAsync(TableEntity accessRequest);

    /// <summary>
    /// Updates an existing access request.
    /// </summary>
    Task UpdateAccessRequestAsync(TableEntity accessRequest);
}
