using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Function for cancelling slots.
/// Refactored to use service layer for business logic.
/// </summary>
public class CancelSlot
{
    private readonly ISlotService _slotService;
    private readonly ILogger _log;

    public CancelSlot(ISlotService slotService, ILoggerFactory lf)
    {
        _slotService = slotService;
        _log = lf.CreateLogger<CancelSlot>();
    }

    [Function("CancelSlot")]
    [OpenApiOperation(operationId: "CancelSlot", tags: new[] { "Slots" }, Summary = "Cancel a slot", Description = "Cancels a slot. Only the slot owner or league admin can cancel a slot.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Division code (e.g., '10U', '12U')")]
    [OpenApiParameter(name: "slotId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Unique slot identifier")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Slot cancelled successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not slot owner or admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Slot not found")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "slots/{division}/{slotId}/cancel")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Validate parameters
            division = (division ?? "").Trim();
            slotId = (slotId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("division", division);
            ApiGuards.EnsureValidTableKeyPart("slotId", slotId);

            // Delegate to service (all authorization and business logic is in the service)
            await _slotService.CancelSlotAsync(leagueId, division, slotId, me.UserId);

            // Track telemetry
            UsageTelemetry.Track(_log, "api_slot_cancel", leagueId, me.UserId, new
            {
                division,
                slotId
            });

            return ApiResponses.Ok(req, new { ok = true, status = Constants.Status.SlotCancelled });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CancelSlot failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
