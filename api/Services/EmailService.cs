using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Stub implementation of IEmailService for email notifications.
/// Currently logs emails instead of sending them. Can be enhanced with SendGrid/SMTP integration.
/// </summary>
public class EmailService : IEmailService
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<EmailService> _logger;

    public EmailService(TableServiceClient tableService, ILogger<EmailService> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task QueueEmailAsync(
        string to,
        string subject,
        string body,
        string emailType,
        string? userId = null,
        string? leagueId = null)
    {
        var emailId = Guid.NewGuid().ToString();

        var entity = new TableEntity(Constants.Pk.EmailQueue("Pending"), emailId)
        {
            ["To"] = to,
            ["Subject"] = subject,
            ["Body"] = body,
            ["EmailType"] = emailType,
            ["UserId"] = userId ?? "",
            ["LeagueId"] = leagueId ?? "",
            ["QueuedUtc"] = DateTime.UtcNow,
            ["SentUtc"] = null,
            ["AttemptCount"] = 0,
            ["LastError"] = "",
            ["RetryAfterUtc"] = null
        };

        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.EmailQueue);
        await table.AddEntityAsync(entity);

        _logger.LogInformation(
            "Queued email {EmailId} to {To} with subject '{Subject}' (type: {EmailType})",
            emailId, to, subject, emailType);

        // TODO: Trigger email sending function or use a queue trigger
    }

    public async Task SendSlotCreatedEmailAsync(string to, string leagueId, string division, string gameDate, string startTime, string field)
    {
        var subject = $"New Game Slot Available - {gameDate} at {startTime}";
        var body = $@"
A new game slot has been created in your division:

Division: {division}
Date: {gameDate}
Time: {startTime}
Field: {field}

Visit your GameSwap dashboard to view and respond to this opportunity.
";

        await QueueEmailAsync(to, subject, body, "SlotCreated", null, leagueId);
    }

    public async Task SendRequestReceivedEmailAsync(string to, string leagueId, string requesterName, string gameDate, string startTime)
    {
        var subject = $"Swap Request Received - {gameDate} at {startTime}";
        var body = $@"
You have received a new swap request:

From: {requesterName}
Date: {gameDate}
Time: {startTime}

Visit your GameSwap dashboard to review and respond to this request.
";

        await QueueEmailAsync(to, subject, body, "RequestReceived", null, leagueId);
    }

    public async Task SendRequestApprovedEmailAsync(string to, string leagueId, string gameDate, string startTime, string field)
    {
        var subject = $"Swap Request Approved - {gameDate} at {startTime}";
        var body = $@"
Your swap request has been approved!

Date: {gameDate}
Time: {startTime}
Field: {field}

The game is now confirmed for your team. Visit your GameSwap dashboard to view details.
";

        await QueueEmailAsync(to, subject, body, "RequestApproved", null, leagueId);
    }

    public async Task SendRequestDeniedEmailAsync(string to, string leagueId, string gameDate, string startTime)
    {
        var subject = $"Swap Request Update - {gameDate} at {startTime}";
        var body = $@"
Your swap request for {gameDate} at {startTime} was not approved.

Visit your GameSwap dashboard to view other available opportunities.
";

        await QueueEmailAsync(to, subject, body, "RequestDenied", null, leagueId);
    }

    public async Task SendGameReminderEmailAsync(string to, string leagueId, string gameDate, string startTime, string field)
    {
        var subject = $"Game Reminder - {gameDate} at {startTime}";
        var body = $@"
Reminder: Your team has a game coming up!

Date: {gameDate}
Time: {startTime}
Field: {field}

See you on the field!
";

        await QueueEmailAsync(to, subject, body, "GameReminder", null, leagueId);
    }
}
