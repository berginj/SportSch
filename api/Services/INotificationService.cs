namespace GameSwap.Functions.Services;

/// <summary>
/// Service for managing in-app notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Creates a new notification for a user.
    /// </summary>
    Task<string> CreateNotificationAsync(
        string userId,
        string leagueId,
        string type,
        string message,
        string? link = null,
        string? relatedEntityId = null,
        string? relatedEntityType = null);

    /// <summary>
    /// Gets all notifications for a user (paginated).
    /// </summary>
    Task<(List<object> notifications, string? continuationToken)> GetUserNotificationsAsync(
        string userId,
        string leagueId,
        int pageSize = 20,
        string? continuationToken = null,
        bool unreadOnly = false);

    /// <summary>
    /// Gets the count of unread notifications for a user.
    /// </summary>
    Task<int> GetUnreadCountAsync(string userId, string leagueId);

    /// <summary>
    /// Marks a notification as read.
    /// </summary>
    Task MarkAsReadAsync(string userId, string notificationId);

    /// <summary>
    /// Marks all notifications as read for a user.
    /// </summary>
    Task MarkAllAsReadAsync(string userId, string leagueId);

    /// <summary>
    /// Deletes old read notifications (cleanup task).
    /// </summary>
    Task DeleteOldNotificationsAsync(int daysOld = 30);
}
