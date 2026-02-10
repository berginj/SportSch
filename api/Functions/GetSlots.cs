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
    [OpenApiOperation(operationId: "GetSlots", tags: new[] { "Slots" }, Summary = "Query slots with filters", Description = "Retrieves slots with optional filtering by division, status, and date range. Supports pagination.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by division (e.g., '10U', '12U')")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by status ('Open', 'Confirmed', 'Cancelled')")]
    [OpenApiParameter(name: "dateFrom", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by start date (YYYY-MM-DD)")]
    [OpenApiParameter(name: "dateTo", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by end date (YYYY-MM-DD)")]
    [OpenApiParameter(name: "fieldKey", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by field key (parkCode/fieldCode)")]
    [OpenApiParameter(name: "continuationToken", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Pagination continuation token")]
    [OpenApiParameter(name: "pageSize", In = ParameterLocation.Query, Required = false, Type = typeof(int), Description = "Number of results per page (default: 50)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Slots retrieved successfully with pagination info")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a member of this league)")]
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
            var fieldKey = (ApiGuards.GetQueryParam(req, "fieldKey") ?? "").Trim();
            var continuationToken = ApiGuards.GetQueryParam(req, "continuationToken");
            var pageSizeStr = ApiGuards.GetQueryParam(req, "pageSize");
            var pageSize = int.TryParse(pageSizeStr, out var ps) ? ps : 50;
            var returnEnvelope = !string.IsNullOrWhiteSpace(continuationToken) || !string.IsNullOrWhiteSpace(pageSizeStr);

            // Build service request
            var serviceRequest = new SlotQueryRequest
            {
                LeagueId = leagueId,
                Division = division,
                Status = status,
                FromDate = dateFrom,
                ToDate = dateTo,
                FieldKey = fieldKey,
                ContinuationToken = continuationToken,
                PageSize = pageSize,
                ReturnEnvelope = returnEnvelope
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
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An unexpected error occurred",
                new { requestId, exception = ex.GetType().Name, message = ex.Message });
        }
    }
}
