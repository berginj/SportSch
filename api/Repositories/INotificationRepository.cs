using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for notification data access operations.
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// Gets a single notification by user ID and notification ID.
    /// </summary>
    Task<TableEntity?> GetNotificationAsync(string userId, string notificationId);

    /// <summary>
    /// Queries notifications for a user (paginated).
    /// </summary>
    Task<(List<TableEntity> notifications, string? continuationToken)> QueryNotificationsAsync(
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
    /// Creates a new notification.
    /// </summary>
    Task CreateNotificationAsync(TableEntity notification);

    /// <summary>
    /// Updates an existing notification.
    /// </summary>
    Task UpdateNotificationAsync(TableEntity notification);

    /// <summary>
    /// Deletes old read notifications (cleanup task).
    /// </summary>
    Task DeleteOldNotificationsAsync(string userId, int daysOld = 30);
}
