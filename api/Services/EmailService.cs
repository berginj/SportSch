using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of IEmailService for email notifications using SendGrid.
/// Falls back to queue-only mode if SendGrid is not configured.
/// </summary>
public class EmailService : IEmailService
{
    private readonly TableServiceClient _tableService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly SendGridClient? _sendGridClient;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly bool _isConfigured;

    public EmailService(
        TableServiceClient tableService,
        IConfiguration configuration,
        ILogger<EmailService> logger)
    {
        _tableService = tableService;
        _configuration = configuration;
        _logger = logger;

        // Load SendGrid configuration
        var sendGridApiKey = _configuration["SENDGRID_API_KEY"] ?? _configuration["SendGrid:ApiKey"];
        _fromEmail = _configuration["EMAIL_FROM_ADDRESS"] ?? _configuration["SendGrid:FromEmail"] ?? "noreply@gameswap.app";
        _fromName = _configuration["EMAIL_FROM_NAME"] ?? _configuration["SendGrid:FromName"] ?? "GameSwap";

        if (!string.IsNullOrWhiteSpace(sendGridApiKey))
        {
            _sendGridClient = new SendGridClient(sendGridApiKey);
            _isConfigured = true;
            _logger.LogInformation("EmailService configured with SendGrid");
        }
        else
        {
            _logger.LogWarning("SendGrid not configured. Emails will be queued but not sent. Set SENDGRID_API_KEY to enable email sending.");
            _isConfigured = false;
        }
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
        var now = DateTime.UtcNow;

        // If SendGrid is configured, try to send immediately
        if (_isConfigured && _sendGridClient != null)
        {
            try
            {
                await SendEmailViaSendGridAsync(to, subject, body, emailId);

                // Log as sent
                var entity = new TableEntity(Constants.Pk.EmailQueue("Sent"), emailId)
                {
                    ["To"] = to,
                    ["Subject"] = subject,
                    ["Body"] = body,
                    ["EmailType"] = emailType,
                    ["UserId"] = userId ?? "",
                    ["LeagueId"] = leagueId ?? "",
                    ["QueuedUtc"] = now,
                    ["SentUtc"] = now,
                    ["AttemptCount"] = 1,
                    ["LastError"] = "",
                    ["RetryAfterUtc"] = null
                };

                var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.EmailQueue);
                await table.AddEntityAsync(entity);

                _logger.LogInformation(
                    "Sent email {EmailId} to {To} with subject '{Subject}' (type: {EmailType})",
                    emailId, to, subject, emailType);

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send email {EmailId} via SendGrid, queuing for retry", emailId);
                // Fall through to queue for retry
            }
        }

        // Queue email for later sending (SendGrid not configured or send failed)
        var queueEntity = new TableEntity(Constants.Pk.EmailQueue("Pending"), emailId)
        {
            ["To"] = to,
            ["Subject"] = subject,
            ["Body"] = body,
            ["EmailType"] = emailType,
            ["UserId"] = userId ?? "",
            ["LeagueId"] = leagueId ?? "",
            ["QueuedUtc"] = now,
            ["SentUtc"] = null,
            ["AttemptCount"] = 0,
            ["LastError"] = "",
            ["RetryAfterUtc"] = null
        };

        var queueTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.EmailQueue);
        await queueTable.AddEntityAsync(queueEntity);

        _logger.LogInformation(
            "Queued email {EmailId} to {To} with subject '{Subject}' (type: {EmailType})",
            emailId, to, subject, emailType);
    }

    private async Task SendEmailViaSendGridAsync(string to, string subject, string body, string emailId)
    {
        if (_sendGridClient == null)
        {
            throw new InvalidOperationException("SendGrid client not configured");
        }

        var from = new EmailAddress(_fromEmail, _fromName);
        var toAddress = new EmailAddress(to);

        // Determine if body is HTML or plain text
        var isHtml = body.Contains("<html>", StringComparison.OrdinalIgnoreCase) ||
                     body.Contains("<p>", StringComparison.OrdinalIgnoreCase);

        var msg = MailHelper.CreateSingleEmail(
            from,
            toAddress,
            subject,
            isHtml ? null : body,  // plain text
            isHtml ? body : null   // html
        );

        // Add custom headers for tracking
        msg.AddCustomArg("email_id", emailId);

        var response = await _sendGridClient.SendEmailAsync(msg);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Body.ReadAsStringAsync();
            throw new Exception($"SendGrid returned {response.StatusCode}: {responseBody}");
        }
    }

    public async Task SendSlotCreatedEmailAsync(string to, string leagueId, string division, string gameDate, string startTime, string field)
    {
        var subject = $"New Game Slot Available - {gameDate} at {startTime}";
        var body = BuildHtmlEmail(
            "New Game Slot Available",
            $@"
            <p>A new game slot has been created in your division:</p>
            <table style='margin: 20px 0; border-collapse: collapse;'>
                <tr><td style='padding: 8px; font-weight: bold;'>Division:</td><td style='padding: 8px;'>{division}</td></tr>
                <tr><td style='padding: 8px; font-weight: bold;'>Date:</td><td style='padding: 8px;'>{gameDate}</td></tr>
                <tr><td style='padding: 8px; font-weight: bold;'>Time:</td><td style='padding: 8px;'>{startTime}</td></tr>
                <tr><td style='padding: 8px; font-weight: bold;'>Field:</td><td style='padding: 8px;'>{field}</td></tr>
            </table>
            <p>Visit your GameSwap dashboard to view and respond to this opportunity.</p>
            ",
            "View Calendar"
        );

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

    private static string BuildHtmlEmail(string title, string content, string? ctaText = null)
    {
        var cta = string.IsNullOrEmpty(ctaText) ? "" : $@"
            <div style='text-align: center; margin: 30px 0;'>
                <a href='https://gameswap.app' style='background-color: #3b82f6; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: 600;'>{ctaText}</a>
            </div>";

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
</head>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;'>
    <div style='background-color: #f9fafb; border-radius: 8px; padding: 30px; border: 1px solid #e5e7eb;'>
        <div style='text-align: center; margin-bottom: 30px;'>
            <h1 style='color: #1f2937; margin: 0; font-size: 24px;'>GameSwap</h1>
        </div>

        <h2 style='color: #1f2937; margin: 0 0 20px 0; font-size: 20px;'>{title}</h2>

        {content}

        {cta}

        <div style='margin-top: 40px; padding-top: 20px; border-top: 1px solid #e5e7eb; text-align: center; color: #6b7280; font-size: 14px;'>
            <p style='margin: 5px 0;'>You received this email because you have notifications enabled for GameSwap.</p>
            <p style='margin: 5px 0;'><a href='https://gameswap.app/#settings' style='color: #3b82f6; text-decoration: none;'>Manage notification preferences</a></p>
        </div>
    </div>
</body>
</html>";
    }
}
