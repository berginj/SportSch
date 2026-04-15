using System.Net;
using GameSwap.Functions.Models;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Functions for game reschedule request operations.
/// </summary>
public class GameRescheduleRequestFunctions
{
    private readonly IGameRescheduleRequestService _service;

    public GameRescheduleRequestFunctions(IGameRescheduleRequestService service)
    {
        _service = service;
    }

    [Function("CreateGameRescheduleRequest")]
    [OpenApiOperation(operationId: "CreateGameRescheduleRequest", tags: new[] { "Game Reschedule" }, Summary = "Create game reschedule request", Description = "Creates a request to reschedule a confirmed game to a new date/time/field. Requires opponent team approval.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(GameRescheduleRequestCreateRequest), Required = true)]
    public async Task<HttpResponseData> CreateRescheduleRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "game-reschedule/requests")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = await req.ReadFromJsonAsync<GameRescheduleRequestCreateRequest>();

            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid request body.");

            var result = await _service.CreateRescheduleRequestAsync(
                leagueId,
                me.UserId,
                body.Division,
                body.OriginalSlotId,
                body.ProposedSlotId,
                body.Reason);

            return ApiResponses.Ok(req, EntityMappers.MapGameRescheduleRequest(result));
        });

    [Function("GetGameRescheduleRequests")]
    [OpenApiOperation(operationId: "GetGameRescheduleRequests", tags: new[] { "Game Reschedule" }, Summary = "List game reschedule requests", Description = "Returns reschedule requests filtered by status and user's team involvement.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by status")]
    public async Task<HttpResponseData> GetRescheduleRequests(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "game-reschedule/requests")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var status = ApiGuards.GetQueryParam(req, "status");

            var requests = await _service.QueryRequestsAsync(leagueId, me.UserId, status);
            var mapped = requests.Select(EntityMappers.MapGameRescheduleRequest).ToList();

            return ApiResponses.Ok(req, mapped);
        });

    [Function("OpponentApproveGameReschedule")]
    [OpenApiOperation(operationId: "OpponentApproveGameReschedule", tags: new[] { "Game Reschedule" }, Summary = "Opponent approves reschedule", Description = "Opponent team approves the reschedule request, triggering finalization.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(GameRescheduleOpponentDecisionRequest), Required = false)]
    public async Task<HttpResponseData> OpponentApprove(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "game-reschedule/requests/{requestId}/approve")] HttpRequestData req,
        string requestId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = await req.ReadFromJsonAsync<GameRescheduleOpponentDecisionRequest>();

            var result = await _service.OpponentApproveAsync(leagueId, me.UserId, requestId, body?.Response);

            return ApiResponses.Ok(req, EntityMappers.MapGameRescheduleRequest(result));
        });

    [Function("OpponentRejectGameReschedule")]
    [OpenApiOperation(operationId: "OpponentRejectGameReschedule", tags: new[] { "Game Reschedule" }, Summary = "Opponent rejects reschedule", Description = "Opponent team rejects the reschedule request.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(GameRescheduleOpponentDecisionRequest), Required = false)]
    public async Task<HttpResponseData> OpponentReject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "game-reschedule/requests/{requestId}/reject")] HttpRequestData req,
        string requestId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = await req.ReadFromJsonAsync<GameRescheduleOpponentDecisionRequest>();

            var result = await _service.OpponentRejectAsync(leagueId, me.UserId, requestId, body?.Response);

            return ApiResponses.Ok(req, EntityMappers.MapGameRescheduleRequest(result));
        });

    [Function("CancelGameRescheduleRequest")]
    [OpenApiOperation(operationId: "CancelGameRescheduleRequest", tags: new[] { "Game Reschedule" }, Summary = "Cancel reschedule request", Description = "Requesting team cancels the reschedule request.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> CancelRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "game-reschedule/requests/{requestId}/cancel")] HttpRequestData req,
        string requestId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            var result = await _service.CancelAsync(leagueId, me.UserId, requestId);

            return ApiResponses.Ok(req, EntityMappers.MapGameRescheduleRequest(result));
        });

    [Function("CheckGameRescheduleConflicts")]
    [OpenApiOperation(operationId: "CheckGameRescheduleConflicts", tags: new[] { "Game Reschedule" }, Summary = "Check reschedule conflicts", Description = "Checks for schedule conflicts for both teams at the proposed time.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Query, Required = true, Type = typeof(string))]
    [OpenApiParameter(name: "originalSlotId", In = ParameterLocation.Query, Required = true, Type = typeof(string))]
    [OpenApiParameter(name: "proposedSlotId", In = ParameterLocation.Query, Required = true, Type = typeof(string))]
    public async Task<HttpResponseData> CheckConflicts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "game-reschedule/check-conflicts")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var division = ApiGuards.GetQueryParam(req, "division");
            var originalSlotId = ApiGuards.GetQueryParam(req, "originalSlotId");
            var proposedSlotId = ApiGuards.GetQueryParam(req, "proposedSlotId");

            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "division query parameter is required");
            if (string.IsNullOrWhiteSpace(originalSlotId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "originalSlotId query parameter is required");
            if (string.IsNullOrWhiteSpace(proposedSlotId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "proposedSlotId query parameter is required");

            var result = await _service.CheckConflictsAsync(leagueId, division, originalSlotId, proposedSlotId);

            return ApiResponses.Ok(req, result);
        });

    private static async Task<HttpResponseData> ExecuteAsync(HttpRequestData req, Func<Task<HttpResponseData>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception)
        {
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred.");
        }
    }
}
