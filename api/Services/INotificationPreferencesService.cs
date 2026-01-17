namespace GameSwap.Functions.Services;

/// <summary>
/// Service for managing notification preferences.
/// </summary>
public interface INotificationPreferencesService
{
    /// <summary>
    /// Gets notification preferences for a user and league.
    /// </summary>
    Task<object> GetPreferencesAsync(string userId, string leagueId);

    /// <summary>
    /// Updates notification preferences for a user and league.
    /// </summary>
    Task UpdatePreferencesAsync(string userId, string leagueId, NotificationPreferencesUpdate update);

    /// <summary>
    /// Checks if a user should receive an email notification for a specific type.
    /// </summary>
    Task<bool> ShouldSendEmailAsync(string userId, string leagueId, string notificationType);
}

public record NotificationPreferencesUpdate(
    bool? EnableInAppNotifications,
    bool? EnableEmailNotifications,
    bool? EmailOnSlotCreated,
    bool? EmailOnSlotCancelled,
    bool? EmailOnRequestReceived,
    bool? EmailOnRequestApproved,
    bool? EmailOnRequestDenied,
    bool? EmailOnGameReminder,
    bool? EnableDailyDigest,
    string? DigestTime
);
