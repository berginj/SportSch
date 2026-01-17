using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository interface for league data access.
/// </summary>
public interface ILeagueRepository
{
    /// <summary>
    /// Gets a league by leagueId.
    /// </summary>
    Task<TableEntity?> GetLeagueAsync(string leagueId);

    /// <summary>
    /// Queries all leagues with optional status filter.
    /// </summary>
    Task<List<TableEntity>> QueryLeaguesAsync(bool includeAll = false);

    /// <summary>
    /// Creates a new league.
    /// </summary>
    Task CreateLeagueAsync(TableEntity league);

    /// <summary>
    /// Updates an existing league.
    /// </summary>
    Task UpdateLeagueAsync(TableEntity league);

    /// <summary>
    /// Deletes a league.
    /// </summary>
    Task DeleteLeagueAsync(string leagueId);
}
