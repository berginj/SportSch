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
    // Creates a pending request; slot owner/admin approves later.
    [Function("CreateSlotRequest")]
    [OpenApiOperation(operationId: "CreateSlotRequest", tags: new[] { "Slot Requests" }, Summary = "Create a slot request", Description = "Creates a pending request to accept/swap a slot. The slot remains pending until approved.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Division code (e.g., '10U', '12U')")]
    [OpenApiParameter(name: "slotId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Unique slot identifier")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreateReq), Required = false, Description = "Optional request details (notes, requestingTeamId, requestingDivision)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Request created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request (missing required fields)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a member or coach without team)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Slot not found")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Conflict, contentType: "application/json", bodyType: typeof(object), Description = "Request already exists or slot is not available")]
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
