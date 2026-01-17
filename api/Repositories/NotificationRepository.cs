using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of INotificationRepository for accessing notification data in Table Storage.
/// </summary>
public class NotificationRepository : INotificationRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<NotificationRepository> _logger;
    private const string TableName = Constants.Tables.Notifications;

    public NotificationRepository(TableServiceClient tableService, ILogger<NotificationRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetNotificationAsync(string userId, string notificationId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.Notifications(userId);
        var rk = notificationId;

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, rk);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Notification not found: {UserId}/{NotificationId}", userId, notificationId);
            return null;
        }
    }

    public async Task<(List<TableEntity> notifications, string? continuationToken)> QueryNotificationsAsync(
        string userId,
        string leagueId,
        int pageSize = 20,
        string? continuationToken = null,
        bool unreadOnly = false)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.Notifications(userId);

        // Build filter
        var filters = new List<string>
        {
            ODataFilterBuilder.PartitionKeyExact(pk),
            $"LeagueId eq '{ApiGuards.EscapeOData(leagueId)}'"
        };

        if (unreadOnly)
        {
            filters.Add("IsRead eq false");
        }

        var filter = ODataFilterBuilder.And(filters.ToArray());

        var result = new List<TableEntity>();
        await foreach (var page in table.QueryAsync<TableEntity>(filter: filter).AsPages(continuationToken, pageSize))
        {
            result.AddRange(page.Values);
            return (result, page.ContinuationToken);
        }

        return (result, null);
    }

    public async Task<int> GetUnreadCountAsync(string userId, string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.Notifications(userId);

        var filter = ODataFilterBuilder.And(
            ODataFilterBuilder.PartitionKeyExact(pk),
            $"LeagueId eq '{ApiGuards.EscapeOData(leagueId)}'",
            "IsRead eq false"
        );

        var count = 0;
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter, select: new[] { "PartitionKey" }))
        {
            count++;
        }

        return count;
    }

    public async Task CreateNotificationAsync(TableEntity notification)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.AddEntityAsync(notification);

        _logger.LogInformation("Created notification: {PartitionKey}/{RowKey}", notification.PartitionKey, notification.RowKey);
    }

    public async Task UpdateNotificationAsync(TableEntity notification)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.UpdateEntityAsync(notification, notification.ETag, TableUpdateMode.Replace);

        _logger.LogDebug("Updated notification: {PartitionKey}/{RowKey}", notification.PartitionKey, notification.RowKey);
    }

    public async Task DeleteOldNotificationsAsync(string userId, int daysOld = 30)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.Notifications(userId);

        var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);

        var filter = ODataFilterBuilder.And(
            ODataFilterBuilder.PartitionKeyExact(pk),
            "IsRead eq true",
            $"CreatedUtc lt datetime'{cutoffDate:yyyy-MM-ddTHH:mm:ssZ}'"
        );

        var toDelete = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            toDelete.Add(entity);
        }

        foreach (var entity in toDelete)
        {
            try
            {
                await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogDebug("Notification already deleted: {PartitionKey}/{RowKey}", entity.PartitionKey, entity.RowKey);
            }
        }

        _logger.LogInformation("Deleted {Count} old notifications for user {UserId}", toDelete.Count, userId);
    }
}
