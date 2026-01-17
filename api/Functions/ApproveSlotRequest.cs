using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Function for approving slot requests.
/// Refactored to use service layer for business logic.
/// </summary>
public class ApproveSlotRequest
{
    private readonly IRequestService _requestService;
    private readonly ILogger _log;

    public ApproveSlotRequest(IRequestService requestService, ILoggerFactory lf)
    {
        _requestService = requestService;
        _log = lf.CreateLogger<ApproveSlotRequest>();
    }

    public record ApproveReq(string? approvedByEmail);

    [Function("ApproveSlotRequest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "slots/{division}/{slotId}/requests/{requestId}/approve")] HttpRequestData req,
        string division,
        string slotId,
        string requestId)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Normalize and validate route params
            division = (division ?? "").Trim();
            slotId = (slotId ?? "").Trim();
            requestId = (requestId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("division", division);
            ApiGuards.EnsureValidTableKeyPart("slotId", slotId);
            ApiGuards.EnsureValidTableKeyPart("requestId", requestId);

            // Parse optional body
            var body = await HttpUtil.ReadJsonAsync<ApproveReq>(req);
            var approvedBy = (body?.approvedByEmail ?? me.Email ?? "").Trim();

            // Build service request
            var serviceRequest = new Services.ApproveRequestRequest
            {
                LeagueId = leagueId,
                Division = division,
                SlotId = slotId,
                RequestId = requestId,
                ApprovedByEmail = approvedBy
            };

            var context = CorrelationContext.FromRequest(req, leagueId);

            // Delegate to service
            var result = await _requestService.ApproveRequestAsync(serviceRequest, context);

            // Track telemetry
            UsageTelemetry.Track(_log, "api_slot_request_approve", leagueId, me.UserId, new
            {
                division,
                slotId,
                requestId
            });

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ApproveSlotRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
