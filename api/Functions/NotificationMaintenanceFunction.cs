using GameSwap.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Timer-triggered maintenance tasks for notifications.
/// </summary>
public class NotificationMaintenanceFunction
{
    private readonly INotificationService _notificationService;
    private readonly IConfiguration _configuration;
    private readonly ILogger _log;

    public NotificationMaintenanceFunction(
        INotificationService notificationService,
        IConfiguration configuration,
        ILoggerFactory lf)
    {
        _notificationService = notificationService;
        _configuration = configuration;
        _log = lf.CreateLogger<NotificationMaintenanceFunction>();
    }

    /// <summary>
    /// Runs daily at 03:00 UTC and removes old read notifications.
    /// Configure retention with NOTIFICATIONS_RETENTION_DAYS or Notifications:RetentionDays.
    /// </summary>
    [Function("CleanupOldNotifications")]
    public async Task CleanupOldNotifications([TimerTrigger("0 0 3 * * *")] TimerInfo timerInfo)
    {
        _ = timerInfo;
        var configuredDays = _configuration["NOTIFICATIONS_RETENTION_DAYS"] ?? _configuration["Notifications:RetentionDays"];
        var daysOld = int.TryParse(configuredDays, out var parsedDays) && parsedDays > 0
            ? parsedDays
            : 30;

        _log.LogInformation("Starting old notification cleanup with retention {DaysOld} days", daysOld);
        await _notificationService.DeleteOldNotificationsAsync(daysOld);
        _log.LogInformation("Completed old notification cleanup");
    }
}
