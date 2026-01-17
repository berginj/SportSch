namespace GameSwap.Functions.Services;

/// <summary>
/// Service for sending email notifications.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Queues an email for sending.
    /// </summary>
    Task QueueEmailAsync(
        string to,
        string subject,
        string body,
        string emailType,
        string? userId = null,
        string? leagueId = null);

    /// <summary>
    /// Sends a slot created notification email.
    /// </summary>
    Task SendSlotCreatedEmailAsync(string to, string leagueId, string division, string gameDate, string startTime, string field);

    /// <summary>
    /// Sends a request received notification email.
    /// </summary>
    Task SendRequestReceivedEmailAsync(string to, string leagueId, string requesterName, string gameDate, string startTime);

    /// <summary>
    /// Sends a request approved notification email.
    /// </summary>
    Task SendRequestApprovedEmailAsync(string to, string leagueId, string gameDate, string startTime, string field);

    /// <summary>
    /// Sends a request denied notification email.
    /// </summary>
    Task SendRequestDeniedEmailAsync(string to, string leagueId, string gameDate, string startTime);

    /// <summary>
    /// Sends a game reminder email.
    /// </summary>
    Task SendGameReminderEmailAsync(string to, string leagueId, string gameDate, string startTime, string field);
}
