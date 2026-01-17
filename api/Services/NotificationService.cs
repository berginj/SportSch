using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of INotificationService for managing in-app notifications.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepo;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRepository notificationRepo,
        ILogger<NotificationService> logger)
    {
        _notificationRepo = notificationRepo;
        _logger = logger;
    }

    public async Task<string> CreateNotificationAsync(
        string userId,
        string leagueId,
        string type,
        string message,
        string? link = null,
        string? relatedEntityId = null,
        string? relatedEntityType = null)
    {
        var notificationId = Guid.NewGuid().ToString();

        var entity = new TableEntity(Constants.Pk.Notifications(userId), notificationId)
        {
            ["Type"] = type,
            ["Message"] = message,
            ["Link"] = link ?? "",
            ["RelatedEntityId"] = relatedEntityId ?? "",
            ["RelatedEntityType"] = relatedEntityType ?? "",
            ["LeagueId"] = leagueId,
            ["IsRead"] = false,
            ["CreatedUtc"] = DateTime.UtcNow,
            ["ReadUtc"] = null
        };

        await _notificationRepo.CreateNotificationAsync(entity);

        _logger.LogInformation(
            "Created notification {NotificationId} for user {UserId} in league {LeagueId}, type {Type}",
            notificationId, userId, leagueId, type);

        return notificationId;
    }

    public async Task<(List<object> notifications, string? continuationToken)> GetUserNotificationsAsync(
        string userId,
        string leagueId,
        int pageSize = 20,
        string? continuationToken = null,
        bool unreadOnly = false)
    {
        var (entities, token) = await _notificationRepo.QueryNotificationsAsync(
            userId, leagueId, pageSize, continuationToken, unreadOnly);

        var notifications = entities
            .OrderByDescending(e => e.GetDateTime("CreatedUtc") ?? DateTime.MinValue)
            .Select(e => new
            {
                notificationId = e.RowKey,
                type = e.GetString("Type") ?? "",
                message = e.GetString("Message") ?? "",
                link = e.GetString("Link"),
                relatedEntityId = e.GetString("RelatedEntityId"),
                relatedEntityType = e.GetString("RelatedEntityType"),
                leagueId = e.GetString("LeagueId") ?? "",
                isRead = e.GetBoolean("IsRead") ?? false,
                createdUtc = e.GetDateTime("CreatedUtc"),
                readUtc = e.GetDateTime("ReadUtc")
            })
            .Cast<object>()
            .ToList();

        return (notifications, token);
    }

    public async Task<int> GetUnreadCountAsync(string userId, string leagueId)
    {
        return await _notificationRepo.GetUnreadCountAsync(userId, leagueId);
    }

    public async Task MarkAsReadAsync(string userId, string notificationId)
    {
        var notification = await _notificationRepo.GetNotificationAsync(userId, notificationId);
        if (notification == null)
        {
            _logger.LogWarning("Notification not found: {UserId}/{NotificationId}", userId, notificationId);
            return;
        }

        if (notification.GetBoolean("IsRead") == true)
        {
            _logger.LogDebug("Notification already read: {UserId}/{NotificationId}", userId, notificationId);
            return;
        }

        notification["IsRead"] = true;
        notification["ReadUtc"] = DateTime.UtcNow;

        await _notificationRepo.UpdateNotificationAsync(notification);

        _logger.LogDebug("Marked notification as read: {UserId}/{NotificationId}", userId, notificationId);
    }

    public async Task MarkAllAsReadAsync(string userId, string leagueId)
    {
        // Get all unread notifications
        var (entities, _) = await _notificationRepo.QueryNotificationsAsync(
            userId, leagueId, pageSize: 100, continuationToken: null, unreadOnly: true);

        var updateTasks = entities.Select(async entity =>
        {
            entity["IsRead"] = true;
            entity["ReadUtc"] = DateTime.UtcNow;

            try
            {
                await _notificationRepo.UpdateNotificationAsync(entity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark notification as read: {NotificationId}", entity.RowKey);
            }
        });

        await Task.WhenAll(updateTasks);

        _logger.LogInformation("Marked {Count} notifications as read for user {UserId} in league {LeagueId}",
            entities.Count, userId, leagueId);
    }

    public async Task DeleteOldNotificationsAsync(int daysOld = 30)
    {
        // This would typically be called by a timer function for all users
        // For now, it's a placeholder that would need to be enhanced
        _logger.LogInformation("DeleteOldNotificationsAsync called with daysOld={DaysOld}", daysOld);

        // TODO: Implement user iteration or make this per-user via timer function
        // await _notificationRepo.DeleteOldNotificationsAsync(userId, daysOld);
    }
}
