using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository interface for division data access.
/// </summary>
public interface IDivisionRepository
{
    /// <summary>
    /// Gets a division by code.
    /// </summary>
    Task<TableEntity?> GetDivisionAsync(string leagueId, string code);

    /// <summary>
    /// Queries all divisions for a league.
    /// </summary>
    Task<List<TableEntity>> QueryDivisionsAsync(string leagueId);

    /// <summary>
    /// Creates a new division.
    /// </summary>
    Task CreateDivisionAsync(TableEntity division);

    /// <summary>
    /// Updates an existing division.
    /// </summary>
    Task UpdateDivisionAsync(TableEntity division);

    /// <summary>
    /// Gets division templates for a league.
    /// </summary>
    Task<TableEntity?> GetTemplatesAsync(string leagueId);

    /// <summary>
    /// Updates division templates for a league.
    /// </summary>
    Task UpsertTemplatesAsync(TableEntity templates);
}
