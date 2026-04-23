using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Functions for updating slot status (Cancel, Complete, Postpone games).
/// Sends email notifications to affected teams.
/// </summary>
public class SlotStatusFunctions
{
    private readonly TableServiceClient _tableService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly IEmailService _emailService;
    private readonly ILogger _log;

    public SlotStatusFunctions(
        TableServiceClient tableService,
        IMembershipRepository membershipRepo,
        ISlotRepository slotRepo,
        IEmailService emailService,
        ILoggerFactory lf)
    {
        _tableService = tableService;
        _membershipRepo = membershipRepo;
        _slotRepo = slotRepo;
        _emailService = emailService;
        _log = lf.CreateLogger<SlotStatusFunctions>();
    }

    public record UpdateSlotStatusReq(string status, string? reason);

    [Function("UpdateSlotStatus")]
    [OpenApiOperation(operationId: "UpdateSlotStatus", tags: new[] { "Slots" },
        Summary = "Update slot status",
        Description = "Update slot status (Confirmed, Cancelled, Completed, Postponed). Sends notifications to teams for cancellations. Admin only.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
    [OpenApiParameter(name: "slotId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateSlotStatusReq),
        Required = true, Description = "New status and optional reason")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "Status updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json",
        bodyType: typeof(object), Description = "Only league admins can update slot status")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json",
        bodyType: typeof(object), Description = "Slot not found")]
    public async Task<HttpResponseData> UpdateSlotStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "slots/{division}/{slotId}/status")]
        HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can update slot status");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<UpdateSlotStatusReq>(req);
            var newStatus = (body?.status ?? "").Trim();
            var reason = (body?.reason ?? "").Trim();

            // Validate status value
            var validStatuses = new[] {
                Constants.Status.SlotOpen,
                Constants.Status.SlotConfirmed,
                Constants.Status.SlotCancelled,
                Constants.Status.SlotCompleted,
                Constants.Status.SlotPostponed
            };

            if (!validStatuses.Contains(newStatus))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "INVALID_STATUS",
                    $"Invalid status. Must be one of: {string.Join(", ", validStatuses)}");
            }

            // Get slot
            var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
            if (slot == null)
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Slot not found");

            var oldStatus = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();

            // Validate state transition
            if (!IsValidStatusTransition(oldStatus, newStatus))
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, ErrorCodes.INVALID_STATUS_TRANSITION,
                    $"Invalid status transition from {oldStatus} to {newStatus}. " +
                    $"Valid transitions from {oldStatus}: {string.Join(", ", GetValidTransitionsFrom(oldStatus))}");
            }

            // Validate Confirmed status requires ConfirmedTeamId
            if (string.Equals(newStatus, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
            {
                var confirmedTeamId = (slot.GetString("ConfirmedTeamId") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(confirmedTeamId))
                {
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD,
                        "Cannot set status to Confirmed without ConfirmedTeamId. Use the slot acceptance workflow to confirm slots.");
                }
            }

            // Update status
            slot["Status"] = newStatus;
            slot["StatusUpdatedUtc"] = DateTimeOffset.UtcNow;
            slot["StatusUpdatedBy"] = me.UserId;
            if (!string.IsNullOrWhiteSpace(reason))
                slot["StatusReason"] = reason;

            var slotsTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.Slots);
            await slotsTable.UpdateEntityAsync(slot, slot.ETag);

            _log.LogInformation("Updated slot {SlotId} status from {OldStatus} to {NewStatus}",
                slotId, oldStatus, newStatus);

            // Send notifications if game was cancelled
            if (newStatus == Constants.Status.SlotCancelled && oldStatus == Constants.Status.SlotConfirmed)
            {
                await SendCancellationNotifications(leagueId, division, slot, reason);
            }

            return ApiResponses.Ok(req, new
            {
                slotId,
                division,
                oldStatus,
                newStatus,
                reason,
                updatedUtc = DateTimeOffset.UtcNow
            });
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Slot not found");
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateSlotStatus failed for {Division}/{SlotId}", division, slotId);
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static bool IsValidStatusTransition(string fromStatus, string toStatus)
    {
        // Idempotent transitions always allowed
        if (string.Equals(fromStatus, toStatus, StringComparison.OrdinalIgnoreCase))
            return true;

        return (fromStatus.ToLower(), toStatus.ToLower()) switch
        {
            // From Open
            ("open", "confirmed") => true,
            ("open", "cancelled") => true,

            // From Confirmed
            ("confirmed", "cancelled") => true,
            ("confirmed", "completed") => true,
            ("confirmed", "postponed") => true,

            // From Postponed
            ("postponed", "confirmed") => true,  // Rescheduled
            ("postponed", "cancelled") => true,

            // From Completed - no transitions allowed (game is over)
            ("completed", _) => false,

            // From Cancelled - no transitions allowed (can't un-cancel)
            ("cancelled", _) => false,

            // Any other transition not explicitly allowed
            _ => false
        };
    }

    private static IEnumerable<string> GetValidTransitionsFrom(string fromStatus)
    {
        return fromStatus.ToLower() switch
        {
            "open" => new[] { "Confirmed", "Cancelled" },
            "confirmed" => new[] { "Cancelled", "Completed", "Postponed" },
            "postponed" => new[] { "Confirmed", "Cancelled" },
            "completed" => Array.Empty<string>(),
            "cancelled" => Array.Empty<string>(),
            _ => new[] { "Open", "Confirmed", "Cancelled", "Completed", "Postponed" }
        };
    }

    private async Task SendCancellationNotifications(
        string leagueId,
        string division,
        TableEntity slot,
        string reason)
    {
        try
        {
            var homeTeamId = (slot.GetString("HomeTeamId") ?? "").Trim();
            var awayTeamId = ResolveOpponentTeamId(slot);
            var gameDate = (slot.GetString("GameDate") ?? "").Trim();
            var startTime = (slot.GetString("StartTime") ?? "").Trim();
            var field = (slot.GetString("DisplayName") ?? slot.GetString("FieldKey") ?? "TBD").Trim();

            var teamClient = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);

            // Send to home team
            if (!string.IsNullOrWhiteSpace(homeTeamId))
            {
                try
                {
                    var homeTeam = await teamClient.GetEntityAsync<TableEntity>(
                        Constants.Pk.Teams(leagueId, division), homeTeamId);
                    var email = (homeTeam.Value.GetString("PrimaryContactEmail") ?? "").Trim();

                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        await _emailService.SendGameCancelledEmailAsync(
                            to: email,
                            leagueId: leagueId,
                            gameDate: gameDate,
                            startTime: startTime,
                            field: field,
                            reason: reason.Length > 0 ? reason : "Weather/field conditions"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to send cancellation email to home team {TeamId}", homeTeamId);
                }
            }

            // Send to away team
            if (!string.IsNullOrWhiteSpace(awayTeamId))
            {
                try
                {
                    var awayTeam = await teamClient.GetEntityAsync<TableEntity>(
                        Constants.Pk.Teams(leagueId, division), awayTeamId);
                    var email = (awayTeam.Value.GetString("PrimaryContactEmail") ?? "").Trim();

                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        await _emailService.SendGameCancelledEmailAsync(
                            to: email,
                            leagueId: leagueId,
                            gameDate: gameDate,
                            startTime: startTime,
                            field: field,
                            reason: reason.Length > 0 ? reason : "Weather/field conditions"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to send cancellation email to away team {TeamId}", awayTeamId);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send cancellation notifications for slot");
            // Don't fail the request if email fails
        }
    }

    private static string ResolveOpponentTeamId(TableEntity slot)
    {
        var awayTeamId = (slot.GetString("AwayTeamId") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(awayTeamId))
            return awayTeamId;

        return (slot.GetString("ConfirmedTeamId") ?? "").Trim();
    }
}
