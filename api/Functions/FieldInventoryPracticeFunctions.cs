using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace GameSwap.Functions.Functions;

public class FieldInventoryPracticeFunctions
{
    private readonly IFieldInventoryPracticeService _service;
    private readonly IPracticeAvailabilityService _availabilityService;
    private readonly TableServiceClient _tableService;

    public FieldInventoryPracticeFunctions(
        IFieldInventoryPracticeService service,
        IPracticeAvailabilityService availabilityService,
        TableServiceClient tableService)
    {
        _service = service;
        _availabilityService = availabilityService;
        _tableService = tableService;
    }

    [Function("GetFieldInventoryPracticeAdmin")]
    [OpenApiOperation(operationId: "GetFieldInventoryPracticeAdmin", tags: new[] { "Field Inventory Practice" }, Summary = "Get inventory-backed practice admin view", Description = "Returns committed field inventory aligned to canonical fields/divisions/teams plus pending practice-space requests and policy state.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> GetAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "field-inventory/practice/admin")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var seasonLabel = ApiGuards.GetQueryParam(req, "seasonLabel");
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.GetAdminViewAsync(seasonLabel, context));
        });

    [Function("GetFieldInventoryPracticeCoach")]
    [OpenApiOperation(operationId: "GetFieldInventoryPracticeCoach", tags: new[] { "Field Inventory Practice" }, Summary = "Get coach practice-space view", Description = "Returns requestable 90-minute practice blocks derived from committed field inventory along with the coach team's current requests.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> GetCoach(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "field-inventory/practice/coach")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var seasonLabel = ApiGuards.GetQueryParam(req, "seasonLabel");
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.GetCoachViewAsync(seasonLabel, me.UserId, context));
        });

    [Function("ListPracticeAvailabilityOptions")]
    [OpenApiOperation(operationId: "ListPracticeAvailabilityOptions", tags: new[] { "Field Inventory Practice" }, Summary = "List practice availability options", Description = "Returns canonical practice booking options for a specific day, optionally narrowed to an exact time and field. Coaches are scoped to their own division; admins must pass a division.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> ListAvailabilityOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "field-inventory/practice/availability/options")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = new PracticeAvailabilityQueryRequest(
                ApiGuards.GetQueryParam(req, "seasonLabel"),
                ApiGuards.GetQueryParam(req, "date"),
                ApiGuards.GetQueryParam(req, "startTime"),
                ApiGuards.GetQueryParam(req, "endTime"),
                ApiGuards.GetQueryParam(req, "division"),
                ApiGuards.GetQueryParam(req, "fieldKey"));
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _availabilityService.GetCoachAvailabilityOptionsAsync(body, me.UserId, context));
        });

    [Function("CheckPracticeAvailability")]
    [OpenApiOperation(operationId: "CheckPracticeAvailability", tags: new[] { "Field Inventory Practice" }, Summary = "Check practice availability", Description = "Returns whether an exact practice day/time window is available and includes matching options. Coaches are scoped to their own division; admins must pass a division.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> CheckAvailability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "field-inventory/practice/availability/check")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = new PracticeAvailabilityQueryRequest(
                ApiGuards.GetQueryParam(req, "seasonLabel"),
                ApiGuards.GetQueryParam(req, "date"),
                ApiGuards.GetQueryParam(req, "startTime"),
                ApiGuards.GetQueryParam(req, "endTime"),
                ApiGuards.GetQueryParam(req, "division"),
                ApiGuards.GetQueryParam(req, "fieldKey"));
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _availabilityService.CheckCoachAvailabilityAsync(body, me.UserId, context));
        });

    [Function("SaveFieldInventoryDivisionAlias")]
    [OpenApiOperation(operationId: "SaveFieldInventoryDivisionAlias", tags: new[] { "Field Inventory Practice" }, Summary = "Save a division mapping", Description = "Persists a reusable mapping from imported division text to a canonical SportsCH division.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryDivisionAliasSaveRequest), Required = true)]
    public async Task<HttpResponseData> SaveDivisionAlias(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/practice/mappings/divisions")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryDivisionAliasSaveRequest>();
            if (body is null) return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.SaveDivisionAliasAsync(body, me.UserId, context));
        });

    [Function("SaveFieldInventoryTeamAlias")]
    [OpenApiOperation(operationId: "SaveFieldInventoryTeamAlias", tags: new[] { "Field Inventory Practice" }, Summary = "Save a team mapping", Description = "Persists a reusable mapping from imported team/event text to a canonical SportsCH team.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryTeamAliasSaveRequest), Required = true)]
    public async Task<HttpResponseData> SaveTeamAlias(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/practice/mappings/teams")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryTeamAliasSaveRequest>();
            if (body is null) return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.SaveTeamAliasAsync(body, me.UserId, context));
        });

    [Function("SaveFieldInventoryGroupPolicy")]
    [OpenApiOperation(operationId: "SaveFieldInventoryGroupPolicy", tags: new[] { "Field Inventory Practice" }, Summary = "Save a group booking policy", Description = "Persists a reusable booking policy for an imported group label such as Ponytail.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryGroupPolicySaveRequest), Required = true)]
    public async Task<HttpResponseData> SaveGroupPolicy(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/practice/policies")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryGroupPolicySaveRequest>();
            if (body is null) return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.SaveGroupPolicyAsync(body, me.UserId, context));
        });

    [Function("NormalizeFieldInventoryPracticeAvailability")]
    [OpenApiOperation(operationId: "NormalizeFieldInventoryPracticeAvailability", tags: new[] { "Field Inventory Practice" }, Summary = "Normalize committed inventory into canonical availability", Description = "Creates or refreshes canonical GameSwap availability slots from committed field inventory and returns normalization results plus the refreshed admin view.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryPracticeNormalizeRequest), Required = false)]
    public async Task<HttpResponseData> NormalizeAvailability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/practice/normalize")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryPracticeNormalizeRequest>() ?? new FieldInventoryPracticeNormalizeRequest(null, null, null, null, false);
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.NormalizeAvailabilityAsync(body, me.UserId, context));
        });

    [Function("CreateFieldInventoryPracticeRequest")]
    [OpenApiOperation(operationId: "CreateFieldInventoryPracticeRequest", tags: new[] { "Field Inventory Practice" }, Summary = "Create a practice-space request", Description = "Creates an inventory-backed practice-space request. Ponytail-assigned space auto-approves; unassigned available space enters commissioner review.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryPracticeRequestCreateRequest), Required = true)]
    public async Task<HttpResponseData> CreatePracticeRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/practice/requests")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = await req.ReadFromJsonAsync<FieldInventoryPracticeRequestCreateRequest>();
            if (body is null) return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.CreatePracticeRequestAsync(body, me.UserId, context));
        });

    [Function("MoveFieldInventoryPracticeRequest")]
    [OpenApiOperation(operationId: "MoveFieldInventoryPracticeRequest", tags: new[] { "Field Inventory Practice" }, Summary = "Move a practice-space request", Description = "Creates a replacement request against another normalized practice slot and releases the original request when the move is approved.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryPracticeRequestMoveRequest), Required = true)]
    public async Task<HttpResponseData> MovePracticeRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "field-inventory/practice/requests/{requestId}/move")] HttpRequestData req,
        string requestId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var body = await req.ReadFromJsonAsync<FieldInventoryPracticeRequestMoveRequest>();
            if (body is null) return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.MovePracticeRequestAsync(requestId, body, me.UserId, context));
        });

    [Function("ApproveFieldInventoryPracticeRequest")]
    [OpenApiOperation(operationId: "ApproveFieldInventoryPracticeRequest", tags: new[] { "Field Inventory Practice" }, Summary = "Approve a practice-space request", Description = "Approves a pending inventory-backed practice-space request.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryPracticeRequestDecisionRequest), Required = false)]
    public async Task<HttpResponseData> ApprovePracticeRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "field-inventory/practice/requests/{requestId}/approve")] HttpRequestData req,
        string requestId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryPracticeRequestDecisionRequest>() ?? new FieldInventoryPracticeRequestDecisionRequest(null);
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.ApprovePracticeRequestAsync(requestId, body, me.UserId, context));
        });

    [Function("RejectFieldInventoryPracticeRequest")]
    [OpenApiOperation(operationId: "RejectFieldInventoryPracticeRequest", tags: new[] { "Field Inventory Practice" }, Summary = "Reject a practice-space request", Description = "Rejects a pending inventory-backed practice-space request.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryPracticeRequestDecisionRequest), Required = false)]
    public async Task<HttpResponseData> RejectPracticeRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "field-inventory/practice/requests/{requestId}/reject")] HttpRequestData req,
        string requestId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryPracticeRequestDecisionRequest>() ?? new FieldInventoryPracticeRequestDecisionRequest(null);
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.RejectPracticeRequestAsync(requestId, body, me.UserId, context));
        });

    [Function("CancelFieldInventoryPracticeRequest")]
    [OpenApiOperation(operationId: "CancelFieldInventoryPracticeRequest", tags: new[] { "Field Inventory Practice" }, Summary = "Cancel a practice-space request", Description = "Cancels a coach team's practice-space request and releases its reservation on the canonical availability block.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> CancelPracticeRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "field-inventory/practice/requests/{requestId}/cancel")] HttpRequestData req,
        string requestId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            var context = CorrelationContext.FromRequest(req, leagueId);
            return ApiResponses.Ok(req, await _service.CancelPracticeRequestAsync(requestId, me.UserId, context));
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
