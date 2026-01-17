using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository interface for team data access.
/// </summary>
public interface ITeamRepository
{
    /// <summary>
    /// Gets a team by division and teamId.
    /// </summary>
    Task<TableEntity?> GetTeamAsync(string leagueId, string division, string teamId);

    /// <summary>
    /// Queries teams for a specific division.
    /// </summary>
    Task<List<TableEntity>> QueryTeamsByDivisionAsync(string leagueId, string division);

    /// <summary>
    /// Queries all teams across all divisions for a league.
    /// </summary>
    Task<List<TableEntity>> QueryAllTeamsAsync(string leagueId);

    /// <summary>
    /// Creates a new team.
    /// </summary>
    Task CreateTeamAsync(TableEntity team);

    /// <summary>
    /// Updates an existing team.
    /// </summary>
    Task UpdateTeamAsync(TableEntity team);

    /// <summary>
    /// Deletes a team.
    /// </summary>
    Task DeleteTeamAsync(string leagueId, string division, string teamId);
}
