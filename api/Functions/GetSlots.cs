using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Repositories;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Function for querying slots with filtering.
/// Refactored to use service layer for business logic.
/// </summary>
public class GetSlots
{
    private readonly ISlotService _slotService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger _log;

    public GetSlots(ISlotService slotService, IMembershipRepository membershipRepo, ILoggerFactory lf)
    {
        _slotService = slotService;
        _membershipRepo = membershipRepo;
        _log = lf.CreateLogger<GetSlots>();
    }

    [Function("GetSlots")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "slots")] HttpRequestData req)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be a member
            if (!await _membershipRepo.IsMemberAsync(me.UserId, leagueId) &&
                !await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Access denied: no membership for this league");
            }

            // Extract query parameters
            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();
            var status = (ApiGuards.GetQueryParam(req, "status") ?? "").Trim();
            var dateFrom = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateTo = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();
            var continuationToken = ApiGuards.GetQueryParam(req, "continuationToken");
            var pageSizeStr = ApiGuards.GetQueryParam(req, "pageSize");
            var pageSize = int.TryParse(pageSizeStr, out var ps) ? ps : 50;

            // Build service request
            var serviceRequest = new SlotQueryRequest
            {
                LeagueId = leagueId,
                Division = division,
                Status = status,
                FromDate = dateFrom,
                ToDate = dateTo,
                ContinuationToken = continuationToken,
                PageSize = pageSize
            };

            var context = CorrelationContext.FromRequest(req, leagueId);

            // Delegate to service
            var result = await _slotService.QuerySlotsAsync(serviceRequest, context);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetSlots failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
