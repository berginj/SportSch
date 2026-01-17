using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for notification preferences data access operations.
/// </summary>
public interface INotificationPreferencesRepository
{
    /// <summary>
    /// Gets notification preferences for a user and league.
    /// </summary>
    Task<TableEntity?> GetPreferencesAsync(string userId, string leagueId);

    /// <summary>
    /// Gets global notification preferences for a user.
    /// </summary>
    Task<TableEntity?> GetGlobalPreferencesAsync(string userId);

    /// <summary>
    /// Creates or updates notification preferences.
    /// </summary>
    Task UpsertPreferencesAsync(TableEntity preferences);

    /// <summary>
    /// Checks if a user has a specific email notification type enabled.
    /// </summary>
    Task<bool> IsEmailNotificationEnabledAsync(string userId, string leagueId, string notificationType);
}
