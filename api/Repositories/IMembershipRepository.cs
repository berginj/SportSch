using Azure.Data.Tables;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for membership and authorization data access.
/// </summary>
public interface IMembershipRepository
{
    /// <summary>
    /// Gets a user's membership in a specific league.
    /// </summary>
    Task<TableEntity?> GetMembershipAsync(string userId, string leagueId);

    /// <summary>
    /// Checks if a user is a global administrator.
    /// </summary>
    Task<bool> IsGlobalAdminAsync(string userId);

    /// <summary>
    /// Checks if a user is a member of a league.
    /// </summary>
    Task<bool> IsMemberAsync(string userId, string leagueId);

    /// <summary>
    /// Gets all memberships for a user.
    /// </summary>
    Task<List<TableEntity>> GetUserMembershipsAsync(string userId);

    /// <summary>
    /// Gets all memberships in a league with optional pagination.
    /// </summary>
    Task<PaginationResult<TableEntity>> QueryLeagueMembershipsAsync(string leagueId, string? role = null, string? continuationToken = null, int pageSize = 50);

    /// <summary>
    /// Creates a new membership.
    /// </summary>
    Task CreateMembershipAsync(TableEntity membership);

    /// <summary>
    /// Updates an existing membership (e.g., role change, coach assignment).
    /// </summary>
    Task UpdateMembershipAsync(TableEntity membership);

    /// <summary>
    /// Deletes a membership.
    /// </summary>
    Task DeleteMembershipAsync(string userId, string leagueId);

    /// <summary>
    /// Upserts a membership (creates or updates).
    /// </summary>
    Task UpsertMembershipAsync(TableEntity membership);

    /// <summary>
    /// Gets all memberships across all leagues (for global admin).
    /// </summary>
    Task<List<TableEntity>> QueryAllMembershipsAsync(string? leagueFilter = null);
}
