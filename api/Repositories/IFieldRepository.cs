using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for field data access operations.
/// </summary>
public interface IFieldRepository
{
    /// <summary>
    /// Gets a single field by park and field codes.
    /// </summary>
    Task<TableEntity?> GetFieldAsync(string leagueId, string parkCode, string fieldCode);

    /// <summary>
    /// Queries all fields for a league, optionally filtered by park.
    /// </summary>
    Task<List<TableEntity>> QueryFieldsAsync(string leagueId, string? parkCode = null);

    /// <summary>
    /// Checks if a field exists.
    /// </summary>
    Task<bool> FieldExistsAsync(string leagueId, string parkCode, string fieldCode);

    /// <summary>
    /// Creates a new field.
    /// </summary>
    Task CreateFieldAsync(TableEntity field);

    /// <summary>
    /// Updates an existing field.
    /// </summary>
    Task UpdateFieldAsync(TableEntity field);

    /// <summary>
    /// Soft deletes a field by setting IsActive to false.
    /// </summary>
    Task DeactivateFieldAsync(string leagueId, string parkCode, string fieldCode);
}
