using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for umpire profile data access operations.
/// </summary>
public interface IUmpireProfileRepository
{
    /// <summary>
    /// Gets a single umpire profile by user ID.
    /// </summary>
    Task<TableEntity?> GetUmpireAsync(string leagueId, string umpireUserId);

    /// <summary>
    /// Queries all umpires in a league with optional active filter.
    /// </summary>
    /// <param name="leagueId">League identifier</param>
    /// <param name="activeOnly">If true, only return active umpires. If null/false, return all.</param>
    Task<List<TableEntity>> QueryUmpiresAsync(string leagueId, bool? activeOnly = null);

    /// <summary>
    /// Creates a new umpire profile.
    /// </summary>
    Task CreateUmpireAsync(TableEntity umpire);

    /// <summary>
    /// Updates an existing umpire profile with ETag concurrency check.
    /// </summary>
    Task UpdateUmpireAsync(TableEntity umpire, ETag etag);

    /// <summary>
    /// Deletes an umpire profile (soft delete - sets IsActive = false recommended).
    /// </summary>
    Task DeleteUmpireAsync(string leagueId, string umpireUserId);

    /// <summary>
    /// Searches umpires by name (case-insensitive partial match).
    /// </summary>
    Task<List<TableEntity>> SearchUmpiresByNameAsync(string leagueId, string searchTerm);
}
