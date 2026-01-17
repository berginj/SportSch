using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Function for creating slot requests.
/// Refactored to use service layer for business logic.
/// </summary>
public class CreateSlotRequest
{
    private readonly IRequestService _requestService;
    private readonly ILogger _log;

    public CreateSlotRequest(IRequestService requestService, ILoggerFactory lf)
    {
        _requestService = requestService;
        _log = lf.CreateLogger<CreateSlotRequest>();
    }

    public record CreateReq(string? notes, string? requestingTeamId, string? requestingDivision);

    // POST /slots/{division}/{slotId}/requests
    // Accepting a slot immediately confirms it (no approval step).
    [Function("CreateSlotRequest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "slots/{division}/{slotId}/requests")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Normalize route params
            var divisionNorm = (division ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(divisionNorm))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Division is required.");
            }
            ApiGuards.EnsureValidTableKeyPart("division", divisionNorm);

            var slotIdNorm = (slotId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(slotIdNorm))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "slotId is required.");
            }
            ApiGuards.EnsureValidTableKeyPart("slotId", slotIdNorm);

            // Parse optional body
            var body = await HttpUtil.ReadJsonAsync<CreateReq>(req);
            var notes = (body?.notes ?? "").Trim();
            var overrideTeamId = (body?.requestingTeamId ?? "").Trim();
            var overrideDivision = (body?.requestingDivision ?? "").Trim().ToUpperInvariant();

            // Build service request
            var serviceRequest = new Services.CreateRequestRequest
            {
                LeagueId = leagueId,
                Division = divisionNorm,
                SlotId = slotIdNorm,
                Notes = notes,
                RequestingTeamId = overrideTeamId,
                RequestingDivision = overrideDivision
            };

            var context = CorrelationContext.FromRequest(req, leagueId);

            // Delegate to service
            var result = await _requestService.CreateRequestAsync(serviceRequest, context);

            // Track telemetry
            var resultDynamic = (dynamic)result;
            UsageTelemetry.Track(_log, "api_slot_request_accept", leagueId, me.UserId, new
            {
                division = divisionNorm,
                slotId = slotIdNorm,
                requestingTeamId = (string)resultDynamic.requestingTeamId
            });

            return ApiResponses.Ok(req, result, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateSlotRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
