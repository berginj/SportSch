using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of INotificationPreferencesService for managing notification preferences.
/// </summary>
public class NotificationPreferencesService : INotificationPreferencesService
{
    private readonly INotificationPreferencesRepository _preferencesRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger<NotificationPreferencesService> _logger;

    public NotificationPreferencesService(
        INotificationPreferencesRepository preferencesRepo,
        IMembershipRepository membershipRepo,
        ILogger<NotificationPreferencesService> logger)
    {
        _preferencesRepo = preferencesRepo;
        _membershipRepo = membershipRepo;
        _logger = logger;
    }

    public async Task<object> GetPreferencesAsync(string userId, string leagueId)
    {
        // Get preferences (or return defaults if none exist)
        var prefs = await _preferencesRepo.GetPreferencesAsync(userId, leagueId);

        if (prefs == null)
        {
            // Return default preferences
            return new
            {
                userId = userId,
                leagueId = leagueId,
                enableInAppNotifications = true,
                enableEmailNotifications = true,
                emailOnSlotCreated = true,
                emailOnSlotCancelled = true,
                emailOnRequestReceived = true,
                emailOnRequestApproved = true,
                emailOnRequestDenied = true,
                emailOnGameReminder = true,
                enableDailyDigest = false,
                digestTime = (string?)null,
                email = await GetUserEmailAsync(userId, leagueId)
            };
        }

        return new
        {
            userId = userId,
            leagueId = leagueId,
            enableInAppNotifications = prefs.GetBoolean("EnableInAppNotifications") ?? true,
            enableEmailNotifications = prefs.GetBoolean("EnableEmailNotifications") ?? true,
            emailOnSlotCreated = prefs.GetBoolean("EmailOnSlotCreated") ?? true,
            emailOnSlotCancelled = prefs.GetBoolean("EmailOnSlotCancelled") ?? true,
            emailOnRequestReceived = prefs.GetBoolean("EmailOnRequestReceived") ?? true,
            emailOnRequestApproved = prefs.GetBoolean("EmailOnRequestApproved") ?? true,
            emailOnRequestDenied = prefs.GetBoolean("EmailOnRequestDenied") ?? true,
            emailOnGameReminder = prefs.GetBoolean("EmailOnGameReminder") ?? true,
            enableDailyDigest = prefs.GetBoolean("EnableDailyDigest") ?? false,
            digestTime = prefs.GetString("DigestTime"),
            email = prefs.GetString("Email") ?? await GetUserEmailAsync(userId, leagueId),
            updatedUtc = prefs.GetDateTime("UpdatedUtc")
        };
    }

    public async Task UpdatePreferencesAsync(string userId, string leagueId, NotificationPreferencesUpdate update)
    {
        _logger.LogInformation("Updating notification preferences for user {UserId} in league {LeagueId}", userId, leagueId);

        // Get existing preferences or create new entity
        var prefs = await _preferencesRepo.GetPreferencesAsync(userId, leagueId);
        var isNew = prefs == null;

        if (isNew)
        {
            var pk = Constants.Pk.NotificationPreferences(userId);
            var rk = leagueId;
            prefs = new TableEntity(pk, rk);
        }

        // Update fields (only update non-null values)
        if (update.EnableInAppNotifications.HasValue)
            prefs["EnableInAppNotifications"] = update.EnableInAppNotifications.Value;

        if (update.EnableEmailNotifications.HasValue)
            prefs["EnableEmailNotifications"] = update.EnableEmailNotifications.Value;

        if (update.EmailOnSlotCreated.HasValue)
            prefs["EmailOnSlotCreated"] = update.EmailOnSlotCreated.Value;

        if (update.EmailOnSlotCancelled.HasValue)
            prefs["EmailOnSlotCancelled"] = update.EmailOnSlotCancelled.Value;

        if (update.EmailOnRequestReceived.HasValue)
            prefs["EmailOnRequestReceived"] = update.EmailOnRequestReceived.Value;

        if (update.EmailOnRequestApproved.HasValue)
            prefs["EmailOnRequestApproved"] = update.EmailOnRequestApproved.Value;

        if (update.EmailOnRequestDenied.HasValue)
            prefs["EmailOnRequestDenied"] = update.EmailOnRequestDenied.Value;

        if (update.EmailOnGameReminder.HasValue)
            prefs["EmailOnGameReminder"] = update.EmailOnGameReminder.Value;

        if (update.EnableDailyDigest.HasValue)
            prefs["EnableDailyDigest"] = update.EnableDailyDigest.Value;

        if (update.DigestTime != null)
            prefs["DigestTime"] = update.DigestTime;

        // Store user email for convenience
        prefs["Email"] = await GetUserEmailAsync(userId, leagueId);
        prefs["UpdatedUtc"] = DateTime.UtcNow;

        await _preferencesRepo.UpsertPreferencesAsync(prefs);

        _logger.LogInformation("Updated notification preferences for user {UserId} in league {LeagueId}", userId, leagueId);
    }

    public async Task<bool> ShouldSendEmailAsync(string userId, string leagueId, string notificationType)
    {
        return await _preferencesRepo.IsEmailNotificationEnabledAsync(userId, leagueId, notificationType);
    }

    private async Task<string> GetUserEmailAsync(string userId, string leagueId)
    {
        try
        {
            var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
            return membership?.GetString("Email") ?? "";
        }
        catch
        {
            return "";
        }
    }
}
