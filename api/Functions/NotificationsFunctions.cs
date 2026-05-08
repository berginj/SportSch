using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Telemetry;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Functions for managing in-app notifications.
/// </summary>
public class NotificationsFunctions
{
    private readonly INotificationService _notificationService;
    private readonly ILogger _log;

    public NotificationsFunctions(INotificationService notificationService, ILoggerFactory lf)
    {
        _notificationService = notificationService;
        _log = lf.CreateLogger<NotificationsFunctions>();
    }

    /// <summary>
    /// GET /api/notifications - Get user notifications (paginated)
    /// Query params: pageSize (default 20), continuationToken, unreadOnly (default false)
    /// </summary>
    [OpenApiOperation(operationId: "GetNotifications", tags: new[] { "Notifications" },
        Summary = "Get user notifications",
        Description = "Retrieve paginated notifications for the current user. Supports filtering by unread status.")]
    [Function("GetNotifications")]
    public async Task<HttpResponseData> GetNotifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var pageSize = int.TryParse(query["pageSize"], out var ps) ? ps : 20;
            var continuationToken = query["continuationToken"];
            var unreadOnly = bool.TryParse(query["unreadOnly"], out var uo) && uo;

            // Delegate to service
            var (notifications, token) = await _notificationService.GetUserNotificationsAsync(
                me.UserId, leagueId, pageSize, continuationToken, unreadOnly);

            var result = new
            {
                items = notifications,
                continuationToken = token,
                pageSize = pageSize
            };

            UsageTelemetry.Track(_log, "api_notifications_get", leagueId, me.UserId, new
            {
                count = notifications.Count,
                unreadOnly = unreadOnly
            });

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetNotifications failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    /// <summary>
    /// GET /api/notifications/unread-count - Get count of unread notifications
    /// </summary>
    [OpenApiOperation(operationId: "GetUnreadNotificationsCount", tags: new[] { "Notifications" },
        Summary = "Get unread notification count",
        Description = "Returns the count of unread notifications for the current user.")]
    [Function("GetUnreadNotificationsCount")]
    public async Task<HttpResponseData> GetUnreadCount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/unread-count")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            var count = await _notificationService.GetUnreadCountAsync(me.UserId, leagueId);

            var result = new { count = count };

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetUnreadCount failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    public record MarkReadRequest(string? notificationId);

    /// <summary>
    /// PATCH /api/notifications/{notificationId}/read - Mark a notification as read
    /// </summary>
    [OpenApiOperation(operationId: "MarkNotificationRead", tags: new[] { "Notifications" },
        Summary = "Mark notification as read",
        Description = "Mark a single notification as read by its ID.")]
    [Function("MarkNotificationRead")]
    public async Task<HttpResponseData> MarkAsRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "notifications/{notificationId}/read")] HttpRequestData req,
        string notificationId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            await _notificationService.MarkAsReadAsync(me.UserId, notificationId);

            UsageTelemetry.Track(_log, "api_notification_mark_read", leagueId, me.UserId, new
            {
                notificationId = notificationId
            });

            return ApiResponses.Ok(req, new { success = true });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MarkAsRead failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    /// <summary>
    /// POST /api/notifications/read-all - Mark all notifications as read
    /// </summary>
    [OpenApiOperation(operationId: "MarkAllNotificationsRead", tags: new[] { "Notifications" },
        Summary = "Mark all notifications as read",
        Description = "Mark all notifications as read for the current user in the active league.")]
    [Function("MarkAllNotificationsRead")]
    public async Task<HttpResponseData> MarkAllAsRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/read-all")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            await _notificationService.MarkAllAsReadAsync(me.UserId, leagueId);

            UsageTelemetry.Track(_log, "api_notifications_mark_all_read", leagueId, me.UserId, null);

            return ApiResponses.Ok(req, new { success = true });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MarkAllAsRead failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
