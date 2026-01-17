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
    /// Gets a field by its composite field key (parkCode/fieldCode).
    /// </summary>
    Task<TableEntity?> GetFieldByKeyAsync(string leagueId, string fieldKey);

    /// <summary>
    /// Queries all fields in a league, optionally filtered by park.
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
    /// Deletes a field.
    /// </summary>
    Task DeleteFieldAsync(string leagueId, string parkCode, string fieldCode);

    /// <summary>
    /// Deactivates a field (sets IsActive to false).
    /// </summary>
    Task DeactivateFieldAsync(string leagueId, string parkCode, string fieldCode);
}
