using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of INotificationPreferencesRepository for accessing notification preferences in Table Storage.
/// </summary>
public class NotificationPreferencesRepository : INotificationPreferencesRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<NotificationPreferencesRepository> _logger;
    private const string TableName = Constants.Tables.NotificationPreferences;

    public NotificationPreferencesRepository(TableServiceClient tableService, ILogger<NotificationPreferencesRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetPreferencesAsync(string userId, string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.NotificationPreferences(userId);
        var rk = leagueId;

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, rk);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Notification preferences not found: {UserId}/{LeagueId}", userId, leagueId);
            return null;
        }
    }

    public async Task<TableEntity?> GetGlobalPreferencesAsync(string userId)
    {
        return await GetPreferencesAsync(userId, "global");
    }

    public async Task UpsertPreferencesAsync(TableEntity preferences)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.UpsertEntityAsync(preferences, TableUpdateMode.Replace);

        _logger.LogInformation("Upserted notification preferences: {PartitionKey}/{RowKey}", preferences.PartitionKey, preferences.RowKey);
    }

    public async Task<bool> IsEmailNotificationEnabledAsync(string userId, string leagueId, string notificationType)
    {
        // Check league-specific preferences first
        var leaguePrefs = await GetPreferencesAsync(userId, leagueId);
        if (leaguePrefs != null)
        {
            // Check if email notifications are globally enabled
            var enableEmail = leaguePrefs.GetBoolean("EnableEmailNotifications") ?? true;
            if (!enableEmail) return false;

            // Check specific notification type
            var fieldName = GetNotificationTypeFieldName(notificationType);
            if (!string.IsNullOrEmpty(fieldName))
            {
                return leaguePrefs.GetBoolean(fieldName) ?? true;
            }

            return true;
        }

        // Fall back to global preferences
        var globalPrefs = await GetGlobalPreferencesAsync(userId);
        if (globalPrefs != null)
        {
            var enableEmail = globalPrefs.GetBoolean("EnableEmailNotifications") ?? true;
            if (!enableEmail) return false;

            var fieldName = GetNotificationTypeFieldName(notificationType);
            if (!string.IsNullOrEmpty(fieldName))
            {
                return globalPrefs.GetBoolean(fieldName) ?? true;
            }

            return true;
        }

        // Default to enabled if no preferences found
        return true;
    }

    private static string GetNotificationTypeFieldName(string notificationType)
    {
        return notificationType switch
        {
            "SlotCreated" => "EmailOnSlotCreated",
            "SlotCancelled" => "EmailOnSlotCancelled",
            "RequestReceived" => "EmailOnRequestReceived",
            "RequestApproved" => "EmailOnRequestApproved",
            "RequestDenied" => "EmailOnRequestDenied",
            "GameReminder" => "EmailOnGameReminder",
            _ => ""
        };
    }
}
