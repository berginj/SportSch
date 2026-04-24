using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service for sending umpire-specific email notifications.
/// Handles assignment notifications, game changes, and cancellations.
/// </summary>
public class UmpireNotificationService
{
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly IUmpireProfileRepository _umpireRepo;
    private readonly ILogger<UmpireNotificationService> _logger;

    public UmpireNotificationService(
        IEmailService emailService,
        INotificationService notificationService,
        IUmpireProfileRepository umpireRepo,
        ILogger<UmpireNotificationService> logger)
    {
        _emailService = emailService;
        _notificationService = notificationService;
        _umpireRepo = umpireRepo;
        _logger = logger;
    }

    /// <summary>
    /// Sends assignment notification when umpire is assigned to a game.
    /// Includes in-app notification + email.
    /// </summary>
    public async Task SendAssignmentNotificationAsync(
        string umpireUserId,
        string leagueId,
        TableEntity assignment)
    {
        var umpire = await _umpireRepo.GetUmpireAsync(leagueId, umpireUserId);
        if (umpire == null)
        {
            _logger.LogWarning("Cannot send assignment notification - umpire not found: {UmpireUserId}", umpireUserId);
            return;
        }

        var umpireName = umpire.GetString("Name") ?? "Umpire";
        var umpireEmail = umpire.GetString("Email") ?? "";

        var gameDate = assignment.GetString("GameDate") ?? "";
        var startTime = assignment.GetString("StartTime") ?? "";
        var endTime = assignment.GetString("EndTime") ?? "";
        var fieldName = assignment.GetString("FieldDisplayName") ?? assignment.GetString("FieldKey") ?? "Field TBD";
        var homeTeam = assignment.GetString("HomeTeamId") ?? "TBD";
        var awayTeam = assignment.GetString("AwayTeamId") ?? "TBD";
        var division = assignment.GetString("Division") ?? "";

        // In-app notification
        var message = $"You've been assigned to officiate {homeTeam} vs {awayTeam} on {gameDate} at {startTime}. Please respond.";
        await _notificationService.CreateNotificationAsync(
            umpireUserId,
            leagueId,
            "UmpireAssigned",
            message,
            "#umpire",
            assignment.RowKey,
            "UmpireAssignment");

        // Email notification
        if (!string.IsNullOrWhiteSpace(umpireEmail))
        {
            var emailSubject = $"Game Assignment - {homeTeam} vs {awayTeam}";
            var emailBody = RenderAssignmentEmail(
                umpireName,
                homeTeam,
                awayTeam,
                gameDate,
                startTime,
                endTime,
                fieldName,
                division);

            try
            {
                await _emailService.QueueEmailAsync(
                    umpireEmail,
                    emailSubject,
                    emailBody,
                    "UmpireAssignment",
                    umpireUserId,
                    leagueId);
                _logger.LogInformation("Sent assignment email to {UmpireEmail} for assignment {AssignmentId}",
                    umpireEmail, assignment.RowKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send assignment email to {UmpireEmail}", umpireEmail);
            }
        }
    }

    /// <summary>
    /// Sends notification when a game is rescheduled.
    /// </summary>
    public async Task SendGameChangedNotificationAsync(
        string umpireUserId,
        string leagueId,
        TableEntity assignment,
        string oldDate,
        string oldTime,
        string oldField,
        string newDate,
        string newTime,
        string newField)
    {
        var umpire = await _umpireRepo.GetUmpireAsync(leagueId, umpireUserId);
        if (umpire == null) return;

        var umpireName = umpire.GetString("Name") ?? "Umpire";
        var umpireEmail = umpire.GetString("Email") ?? "";
        var homeTeam = assignment.GetString("HomeTeamId") ?? "TBD";
        var awayTeam = assignment.GetString("AwayTeamId") ?? "TBD";

        // In-app notification
        var message = $"Game rescheduled: {homeTeam} vs {awayTeam} moved to {newDate} at {newTime} at {newField}. Was: {oldDate} at {oldTime}.";
        await _notificationService.CreateNotificationAsync(
            umpireUserId,
            leagueId,
            "GameRescheduled",
            message,
            "#umpire",
            assignment.RowKey,
            "UmpireAssignment");

        // Email notification
        if (!string.IsNullOrWhiteSpace(umpireEmail))
        {
            var emailSubject = $"Game Rescheduled - {homeTeam} vs {awayTeam}";
            var emailBody = RenderGameChangedEmail(
                umpireName,
                homeTeam,
                awayTeam,
                oldDate,
                oldTime,
                oldField,
                newDate,
                newTime,
                newField);

            try
            {
                await _emailService.QueueEmailAsync(
                    umpireEmail,
                    emailSubject,
                    emailBody,
                    "UmpireAssignment",
                    umpireUserId,
                    leagueId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send game changed email");
            }
        }
    }

    /// <summary>
    /// Sends notification when a game is cancelled.
    /// </summary>
    public async Task SendGameCancelledNotificationAsync(
        string umpireUserId,
        string leagueId,
        TableEntity assignment)
    {
        var umpire = await _umpireRepo.GetUmpireAsync(leagueId, umpireUserId);
        if (umpire == null) return;

        var umpireEmail = umpire.GetString("Email") ?? "";
        var homeTeam = assignment.GetString("HomeTeamId") ?? "TBD";
        var awayTeam = assignment.GetString("AwayTeamId") ?? "TBD";
        var gameDate = assignment.GetString("GameDate") ?? "";
        var startTime = assignment.GetString("StartTime") ?? "";

        // Email notification
        if (!string.IsNullOrWhiteSpace(umpireEmail))
        {
            var emailSubject = $"Game Cancelled - {homeTeam} vs {awayTeam}";
            var emailBody = RenderGameCancelledEmail(
                umpire.GetString("Name") ?? "Umpire",
                homeTeam,
                awayTeam,
                gameDate,
                startTime,
                assignment.GetString("FieldDisplayName") ?? "");

            try
            {
                await _emailService.QueueEmailAsync(
                    umpireEmail,
                    emailSubject,
                    emailBody,
                    "UmpireAssignment",
                    umpireUserId,
                    leagueId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send cancellation email");
            }
        }
    }

    // ============================================
    // EMAIL TEMPLATES
    // ============================================

    private static string RenderAssignmentEmail(
        string umpireName,
        string homeTeam,
        string awayTeam,
        string gameDate,
        string startTime,
        string endTime,
        string fieldName,
        string division)
    {
        var dayOfWeek = DateTime.TryParse(gameDate, out var date)
            ? date.ToString("dddd")
            : "";

        return $@"
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #2c5282;'>Game Assignment</h2>

        <p>Hi {umpireName},</p>

        <p>You've been assigned to officiate the following game:</p>

        <div style='background: #f7fafc; border-left: 4px solid #4299e1; padding: 20px; margin: 20px 0; border-radius: 4px;'>
            <h3 style='margin-top: 0; color: #2c5282;'>{homeTeam} vs {awayTeam}</h3>
            <p style='margin: 8px 0;'><strong>{dayOfWeek}, {FormatDate(gameDate)}</strong></p>
            <p style='margin: 8px 0;'><strong>Time:</strong> {startTime} - {endTime}</p>
            <p style='margin: 8px 0;'><strong>Field:</strong> {fieldName}</p>
            {(!string.IsNullOrWhiteSpace(division) ? $"<p style='margin: 8px 0;'><strong>Division:</strong> {division}</p>" : "")}
        </div>

        <p>
            <a href='{{PortalLink}}' style='display: inline-block; background: #4299e1; color: white; padding: 12px 24px; text-decoration: none; border-radius: 4px; font-weight: bold;'>
                View Assignment & Respond
            </a>
        </p>

        <p style='margin-top: 20px;'>
            Please log in to your umpire portal to accept or decline this assignment.
        </p>

        <hr style='margin: 30px 0; border: none; border-top: 1px solid #e2e8f0;' />

        <p style='color: #718096; font-size: 14px;'>
            Sports Scheduler - Umpire Assignment System
        </p>
    </div>
</body>
</html>";
    }

    private static string RenderGameChangedEmail(
        string umpireName,
        string homeTeam,
        string awayTeam,
        string oldDate,
        string oldTime,
        string oldField,
        string newDate,
        string newTime,
        string newField)
    {
        return $@"
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #d97706;'>⚠️ Game Rescheduled</h2>

        <p>Hi {umpireName},</p>

        <p>Your assigned game has been rescheduled:</p>

        <div style='background: #fffbeb; border-left: 4px solid #f59e0b; padding: 20px; margin: 20px 0; border-radius: 4px;'>
            <h3 style='margin-top: 0; color: #92400e;'>{homeTeam} vs {awayTeam}</h3>

            <div style='margin: 16px 0; padding: 12px; background: #fee2e2; border-radius: 4px;'>
                <p style='margin: 4px 0; text-decoration: line-through; color: #991b1b;'>
                    <strong>OLD:</strong> {FormatDate(oldDate)} at {oldTime}<br/>
                    {oldField}
                </p>
            </div>

            <div style='margin: 16px 0; padding: 12px; background: #d1fae5; border-radius: 4px;'>
                <p style='margin: 4px 0; color: #065f46;'>
                    <strong>NEW:</strong> {FormatDate(newDate)} at {newTime}<br/>
                    {newField}
                </p>
            </div>
        </div>

        <p style='background: #fef3c7; padding: 12px; border-radius: 4px; border-left: 4px solid #f59e0b;'>
            <strong>Your assignment remains confirmed.</strong> If you can no longer make it at the new time, please decline ASAP in your portal.
        </p>

        <p>
            <a href='{{PortalLink}}' style='display: inline-block; background: #d97706; color: white; padding: 12px 24px; text-decoration: none; border-radius: 4px; font-weight: bold;'>
                View Updated Assignment
            </a>
        </p>

        <hr style='margin: 30px 0; border: none; border-top: 1px solid #e2e8f0;' />

        <p style='color: #718096; font-size: 14px;'>
            Sports Scheduler - Umpire Assignment System
        </p>
    </div>
</body>
</html>";
    }

    private static string RenderGameCancelledEmail(
        string umpireName,
        string homeTeam,
        string awayTeam,
        string gameDate,
        string startTime,
        string fieldName)
    {
        return $@"
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #dc2626;'>🚫 Game Cancelled</h2>

        <p>Hi {umpireName},</p>

        <p>The following game has been cancelled:</p>

        <div style='background: #fee2e2; border-left: 4px solid #dc2626; padding: 20px; margin: 20px 0; border-radius: 4px;'>
            <h3 style='margin-top: 0; color: #991b1b;'>{homeTeam} vs {awayTeam}</h3>
            <p style='margin: 8px 0;'><strong>{FormatDate(gameDate)}</strong> at {startTime}</p>
            <p style='margin: 8px 0;'>{fieldName}</p>
            <p style='margin: 16px 0 0 0; padding-top: 12px; border-top: 1px solid #fca5a5;'>
                <strong>Your assignment has been removed.</strong>
            </p>
        </div>

        <p>
            You do not need to take any action. The game has been removed from your schedule.
        </p>

        <p>
            <a href='{{PortalLink}}' style='display: inline-block; background: #6b7280; color: white; padding: 12px 24px; text-decoration: none; border-radius: 4px;'>
                View Your Schedule
            </a>
        </p>

        <hr style='margin: 30px 0; border: none; border-top: 1px solid #e2e8f0;' />

        <p style='color: #718096; font-size: 14px;'>
            Sports Scheduler - Umpire Assignment System
        </p>
    </div>
</body>
</html>";
    }

    private static string FormatDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return dateStr;

        if (DateTime.TryParse(dateStr, out var date))
        {
            return date.ToString("MMMM d, yyyy");  // "June 15, 2026"
        }

        return dateStr;
    }
}
