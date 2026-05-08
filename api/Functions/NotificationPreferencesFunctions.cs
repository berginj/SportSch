using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Functions for managing notification preferences.
/// </summary>
public class NotificationPreferencesFunctions
{
    private readonly INotificationPreferencesService _preferencesService;
    private readonly ILogger _log;

    public NotificationPreferencesFunctions(
        INotificationPreferencesService preferencesService,
        ILoggerFactory lf)
    {
        _preferencesService = preferencesService;
        _log = lf.CreateLogger<NotificationPreferencesFunctions>();
    }

    /// <summary>
    /// GET /api/notifications/preferences - Get user notification preferences
    /// </summary>
    [OpenApiOperation(operationId: "GetNotificationPreferences", tags: new[] { "Notifications" },
        Summary = "Get notification preferences",
        Description = "Retrieve the current user's notification preferences (email toggles, digest settings).")]
    [Function("GetNotificationPreferences")]
    public async Task<HttpResponseData> GetPreferences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/preferences")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            var preferences = await _preferencesService.GetPreferencesAsync(me.UserId, leagueId);

            return ApiResponses.Ok(req, preferences);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetNotificationPreferences failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    public record UpdatePreferencesRequest(
        bool? enableInAppNotifications,
        bool? enableEmailNotifications,
        bool? emailOnSlotCreated,
        bool? emailOnSlotCancelled,
        bool? emailOnRequestReceived,
        bool? emailOnRequestApproved,
        bool? emailOnRequestDenied,
        bool? emailOnGameReminder,
        bool? enableDailyDigest,
        string? digestTime
    );

    /// <summary>
    /// PATCH /api/notifications/preferences - Update user notification preferences
    /// </summary>
    [OpenApiOperation(operationId: "UpdateNotificationPreferences", tags: new[] { "Notifications" },
        Summary = "Update notification preferences",
        Description = "Update the current user's notification preferences (email toggles, digest settings).")]
    [Function("UpdateNotificationPreferences")]
    public async Task<HttpResponseData> UpdatePreferences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "notifications/preferences")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            var body = await HttpUtil.ReadJsonAsync<UpdatePreferencesRequest>(req);
            if (body == null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            var update = new NotificationPreferencesUpdate(
                body.enableInAppNotifications,
                body.enableEmailNotifications,
                body.emailOnSlotCreated,
                body.emailOnSlotCancelled,
                body.emailOnRequestReceived,
                body.emailOnRequestApproved,
                body.emailOnRequestDenied,
                body.emailOnGameReminder,
                body.enableDailyDigest,
                body.digestTime
            );

            await _preferencesService.UpdatePreferencesAsync(me.UserId, leagueId, update);

            return ApiResponses.Ok(req, new { success = true });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateNotificationPreferences failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
