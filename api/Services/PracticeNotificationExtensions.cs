using GameSwap.Functions.Repositories;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Extension methods for practice request notifications.
/// Handles both in-app notifications and email delivery for practice requests.
/// </summary>
public static class PracticeNotificationExtensions
{
    /// <summary>
    /// Notifies coach that their practice request was auto-approved.
    /// Creates in-app notification and optionally sends email.
    /// </summary>
    public static async Task NotifyPracticeAutoApprovedAsync(
        this INotificationService notificationService,
        IEmailService emailService,
        IMembershipRepository membershipRepo,
        ITeamRepository teamRepo,
        IFieldRepository fieldRepo,
        string leagueId,
        string teamId,
        string requestId,
        string date,
        string startTime,
        string endTime,
        string fieldKey,
        ILogger logger)
    {
        try
        {
            // Get team information
            var team = await teamRepo.GetTeamAsync(leagueId, "", teamId);
            var teamName = team?.GetString("Name") ?? teamId;

            // Get field information
            var fields = await fieldRepo.QueryFieldsAsync(leagueId);
            var field = fields.FirstOrDefault(f => f.GetString("FieldKey") == fieldKey);
            var fieldName = field?.GetString("DisplayName") ?? field?.GetString("FieldName") ?? fieldKey;

            // Get coach membership for this team
            var memberships = await membershipRepo.QueryAllMembershipsAsync(leagueId);
            var coachMemberships = memberships
                .Where(m => m.GetString("TeamId") == teamId && m.GetString("Role") == "Coach")
                .ToList();

            foreach (var membership in coachMemberships)
            {
                var userId = membership.PartitionKey;
                var userEmail = membership.GetString("Email") ?? "";

                // Create in-app notification
                await notificationService.CreateNotificationAsync(
                    userId: userId,
                    leagueId: leagueId,
                    type: "practice_approved",
                    message: $"Practice space confirmed at {fieldName} on {date} {startTime}-{endTime}",
                    link: $"#calendar?date={date}",
                    relatedEntityId: requestId,
                    relatedEntityType: "practice_request"
                );

                // Send email notification (optional - only if email configured)
                if (!string.IsNullOrWhiteSpace(userEmail))
                {
                    await emailService.SendPracticeRequestApprovedEmailAsync(
                        to: userEmail,
                        leagueId: leagueId,
                        teamName: teamName,
                        gameDate: date,
                        startTime: startTime,
                        endTime: endTime,
                        field: fieldName
                    );
                }
            }

            logger.LogInformation(
                "Notified {Count} coaches of auto-approved practice: Team={TeamId}, Date={Date}, Field={FieldKey}",
                coachMemberships.Count, teamId, date, fieldKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send practice auto-approval notifications for request {RequestId}", requestId);
            // Don't throw - notification failure shouldn't fail the request
        }
    }

    /// <summary>
    /// Notifies league admins that a practice request needs manual approval.
    /// Creates in-app notifications and sends emails to all league admins.
    /// </summary>
    public static async Task NotifyAdminsOfPendingPracticeAsync(
        this INotificationService notificationService,
        IEmailService emailService,
        IMembershipRepository membershipRepo,
        ITeamRepository teamRepo,
        IFieldRepository fieldRepo,
        string leagueId,
        string teamId,
        string requestId,
        string date,
        string startTime,
        string endTime,
        string fieldKey,
        int conflictCount,
        ILogger logger)
    {
        try
        {
            // Get team information
            var team = await teamRepo.GetTeamAsync(leagueId, "", teamId);
            var teamName = team?.GetString("Name") ?? teamId;

            // Get field information
            var fields = await fieldRepo.QueryFieldsAsync(leagueId);
            var field = fields.FirstOrDefault(f => f.GetString("FieldKey") == fieldKey);
            var fieldName = field?.GetString("DisplayName") ?? field?.GetString("FieldName") ?? fieldKey;

            // Get all league admins
            var memberships = await membershipRepo.QueryAllMembershipsAsync(leagueId);
            var adminMemberships = memberships
                .Where(m => m.GetString("Role") == "LeagueAdmin")
                .ToList();

            var conflictMessage = conflictCount > 0
                ? $" ({conflictCount} conflict{(conflictCount == 1 ? "" : "s")} detected)"
                : "";

            foreach (var membership in adminMemberships)
            {
                var userId = membership.PartitionKey;
                var userEmail = membership.GetString("Email") ?? "";

                // Create in-app notification
                await notificationService.CreateNotificationAsync(
                    userId: userId,
                    leagueId: leagueId,
                    type: "practice_pending_approval",
                    message: $"{teamName} requested practice at {fieldName} on {date} {startTime}-{endTime}{conflictMessage}",
                    link: $"#manage?tab=practice-requests",
                    relatedEntityId: requestId,
                    relatedEntityType: "practice_request"
                );

                // Send email notification to admins
                if (!string.IsNullOrWhiteSpace(userEmail))
                {
                    var subject = conflictCount > 0
                        ? $"Practice Request Needs Approval - {teamName} ({conflictCount} conflict{(conflictCount == 1 ? "" : "s")})"
                        : $"Practice Request Needs Approval - {teamName}";

                    var body = $@"
<h2>Practice Request Requires Your Approval</h2>

<p>A practice request has been submitted that requires manual approval:</p>

<ul>
  <li><strong>Team:</strong> {teamName}</li>
  <li><strong>Date:</strong> {date}</li>
  <li><strong>Time:</strong> {startTime} - {endTime}</li>
  <li><strong>Field:</strong> {fieldName}</li>
  {(conflictCount > 0 ? $"<li><strong>Conflicts:</strong> {conflictCount} existing booking{(conflictCount == 1 ? "" : "s")}</li>" : "")}
</ul>

{(conflictCount > 0 ? "<p><strong>Why approval is needed:</strong> This request conflicts with existing bookings (exclusive bookings or mixed shared/exclusive).</p>" : "<p><strong>Why approval is needed:</strong> This request requires administrative review.</p>")}

<p>To approve or deny this request, please visit the admin panel:</p>

<p><a href=""#manage?tab=practice-requests"">Review Practice Requests</a></p>

<p>Thanks,<br>SportSch System</p>
";

                    await emailService.QueueEmailAsync(
                        to: userEmail,
                        subject: subject,
                        body: body,
                        emailType: "practice_request_pending",
                        userId: userId,
                        leagueId: leagueId
                    );
                }
            }

            logger.LogInformation(
                "Notified {Count} admins of pending practice request: Team={TeamId}, Date={Date}, Conflicts={ConflictCount}",
                adminMemberships.Count, teamId, date, conflictCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send pending practice notifications for request {RequestId}", requestId);
            // Don't throw - notification failure shouldn't fail the request
        }
    }
}
