using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Repositories;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Function for querying slot requests.
/// Refactored to use service layer for business logic.
/// </summary>
public class GetSlotRequests
{
    private readonly IRequestService _requestService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger _log;

    public GetSlotRequests(IRequestService requestService, IMembershipRepository membershipRepo, ILoggerFactory lf)
    {
        _requestService = requestService;
        _membershipRepo = membershipRepo;
        _log = lf.CreateLogger<GetSlotRequests>();
    }

    [Function("GetSlotRequests")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "slots/{division}/{slotId}/requests")] HttpRequestData req,
        string division,
        string slotId)
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

            // Normalize and validate route params
            division = (division ?? "").Trim();
            slotId = (slotId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("division", division);
            ApiGuards.EnsureValidTableKeyPart("slotId", slotId);

            // Delegate to service
            var result = await _requestService.QueryRequestsAsync(leagueId, division, slotId);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetSlotRequests failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
