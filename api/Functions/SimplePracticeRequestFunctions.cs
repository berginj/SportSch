using System.Net;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Simplified calendar-integrated practice request functions.
/// Supports auto-approval and real-time conflict detection.
/// </summary>
public class SimplePracticeRequestFunctions
{
    private readonly IPracticeRequestService _practiceService;
    private readonly IPracticeRequestRepository _practiceRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IAuthorizationService _authService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SimplePracticeRequestFunctions> _logger;

    public SimplePracticeRequestFunctions(
        IPracticeRequestService practiceService,
        IPracticeRequestRepository practiceRepo,
        IMembershipRepository membershipRepo,
        IAuthorizationService authService,
        INotificationService notificationService,
        ILogger<SimplePracticeRequestFunctions> logger)
    {
        _practiceService = practiceService;
        _practiceRepo = practiceRepo;
        _membershipRepo = membershipRepo;
        _authService = authService;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Checks for conflicts with a proposed practice time.
    /// Used for real-time feedback in the UI.
    /// </summary>
    [Function("CheckPracticeConflicts")]
    public async Task<HttpResponseData> CheckConflicts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "practice/check-conflicts")]
        HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be coach or admin
            await _authService.ValidateNotViewerAsync(me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<CheckConflictsRequest>(req);
            if (body == null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid request body");

            // Get coach's team ID
            var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
            var teamId = membership?.GetString("TeamId") ?? "";

            // Check conflicts
            var conflicts = await SimplePracticeRequestExtensions.CheckSimplePracticeConflictsAsync(
                body.FieldKey ?? "",
                body.Date ?? "",
                body.StartTime ?? "",
                body.EndTime ?? "",
                body.Policy,
                leagueId,
                teamId, // Exclude same team
                _practiceRepo,
                _logger
            );

            return ApiResponses.Ok(req, new
            {
                conflicts = conflicts.Select(c => new
                {
                    requestId = c.RequestId,
                    teamId = c.TeamId,
                    teamName = c.TeamName,
                    startTime = c.StartTime,
                    endTime = c.EndTime,
                    policy = c.Policy,
                    status = c.Status
                }).ToList(),
                canAutoApprove = conflicts.Count == 0 ||
                    (body.Policy == "shared" && conflicts.All(c => c.Policy == "shared"))
            });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckPracticeConflicts failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR,
                "Failed to check conflicts");
        }
    }

    /// <summary>
    /// Creates a simplified practice request from calendar interaction.
    /// Auto-approves if no conflicts.
    /// </summary>
    [Function("CreateSimplePracticeRequest")]
    public async Task<HttpResponseData> CreateSimpleRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "practice/requests")]
        HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be coach or admin
            await _authService.ValidateNotViewerAsync(me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<SimplePracticeRequestParams>(req);
            if (body == null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid request body");

            // Get coach's team ID and division
            var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
            if (membership == null)
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN, "No membership found");

            var teamId = membership.GetString("TeamId") ?? "";
            var division = membership.GetString("Division") ?? "";

            if (string.IsNullOrWhiteSpace(teamId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.COACH_TEAM_REQUIRED,
                    "Coach must be assigned to a team");

            // Create practice request with auto-approval
            var result = await _practiceService.CreateSimplePracticeRequestAsync(
                body,
                leagueId,
                me.UserId,
                teamId,
                _membershipRepo,
                _practiceRepo,
                _logger
            );

            // Send notifications
            if (result.AutoApproved)
            {
                await _notificationService.NotifyPracticeAutoApprovedAsync(
                    leagueId,
                    teamId,
                    result.RequestId,
                    result.Date,
                    result.StartTime,
                    result.FieldKey
                );
            }
            else
            {
                // Notify admins of pending request
                await _notificationService.NotifyAdminsOfPendingPracticeAsync(
                    leagueId,
                    teamId,
                    result.RequestId,
                    result.Date,
                    result.StartTime,
                    result.FieldKey,
                    result.Conflicts.Count
                );
            }

            return ApiResponses.Ok(req, new
            {
                requestId = result.RequestId,
                fieldKey = result.FieldKey,
                date = result.Date,
                startTime = result.StartTime,
                endTime = result.EndTime,
                policy = result.Policy,
                status = result.Status,
                autoApproved = result.AutoApproved,
                conflicts = result.Conflicts.Select(c => new
                {
                    requestId = c.RequestId,
                    teamId = c.TeamId,
                    teamName = c.TeamName,
                    startTime = c.StartTime,
                    endTime = c.EndTime,
                    policy = c.Policy
                }).ToList(),
                message = result.AutoApproved
                    ? "Practice space confirmed! No conflicts detected."
                    : $"Practice request submitted for admin approval. {result.Conflicts.Count} conflict(s) detected."
            }, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateSimplePracticeRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR,
                "Failed to create practice request");
        }
    }

    private record CheckConflictsRequest(
        string? FieldKey,
        string? Date,
        string? StartTime,
        string? EndTime,
        string? Policy
    );
}
