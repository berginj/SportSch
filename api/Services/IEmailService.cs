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

    /// <summary>
    /// Sends a coach onboarding link email.
    /// </summary>
    Task SendCoachOnboardingEmailAsync(string to, string leagueId, string teamName, string onboardingLink);

    /// <summary>
    /// Sends practice request approved notification email.
    /// </summary>
    Task SendPracticeRequestApprovedEmailAsync(string to, string leagueId, string teamName, string gameDate, string startTime, string endTime, string field);

    /// <summary>
    /// Sends practice request rejected notification email.
    /// </summary>
    Task SendPracticeRequestRejectedEmailAsync(string to, string leagueId, string teamName, string gameDate, string startTime, string endTime, string reason);

    /// <summary>
    /// Sends schedule published notification email.
    /// </summary>
    Task SendSchedulePublishedEmailAsync(string to, string leagueId, string division, int gameCount);

    /// <summary>
    /// Sends game cancellation notification email.
    /// </summary>
    Task SendGameCancelledEmailAsync(string to, string leagueId, string gameDate, string startTime, string field, string reason);
}
