using System.Net;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Functions for umpire profile management (admin operations).
/// </summary>
public class UmpireManagementFunctions
{
    private readonly IUmpireService _umpireService;
    private readonly Azure.Data.Tables.TableServiceClient _tableService;
    private readonly ILogger _log;

    public UmpireManagementFunctions(
        IUmpireService umpireService,
        Azure.Data.Tables.TableServiceClient tableService,
        ILoggerFactory loggerFactory)
    {
        _umpireService = umpireService;
        _tableService = tableService;
        _log = loggerFactory.CreateLogger<UmpireManagementFunctions>();
    }

    [Function("CreateUmpire")]
    public async Task<HttpResponseData> CreateUmpire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "umpires")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Verify LeagueAdmin
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<CreateUmpireRequest>(req);
            var context = new CorrelationContext
            {
                UserId = me.UserId,
                UserEmail = me.Email,
                LeagueId = leagueId,
                CorrelationId = req.FunctionContext.InvocationId.ToString()
            };

            var result = await _umpireService.CreateUmpireAsync(body, context);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateUmpire failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("GetUmpires")]
    public async Task<HttpResponseData> GetUmpires(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Verify LeagueAdmin
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var activeOnly = ApiGuards.GetQueryParam(req, "active") == "true";
            var searchTerm = ApiGuards.GetQueryParam(req, "search");

            var filter = new UmpireQueryFilter
            {
                ActiveOnly = activeOnly ? true : null,
                SearchTerm = searchTerm
            };

            var result = await _umpireService.QueryUmpiresAsync(leagueId, filter);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetUmpires failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("GetUmpire")]
    public async Task<HttpResponseData> GetUmpire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires/{umpireUserId}")] HttpRequestData req,
        string umpireUserId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization: LeagueAdmin OR self (umpire viewing own profile)
            var isSelf = string.Equals(me.UserId, umpireUserId, StringComparison.OrdinalIgnoreCase);
            if (!isSelf)
            {
                await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            }

            var result = await _umpireService.GetUmpireAsync(leagueId, umpireUserId);

            if (result == null)
                return ApiResponses.Error(req, HttpStatusCode.NotFound, ErrorCodes.UMPIRE_NOT_FOUND, "Umpire not found");

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetUmpire failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("UpdateUmpire")]
    public async Task<HttpResponseData> UpdateUmpire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "umpires/{umpireUserId}")] HttpRequestData req,
        string umpireUserId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            var body = await HttpUtil.ReadJsonAsync<UpdateUmpireRequest>(req);
            var context = new CorrelationContext
            {
                UserId = me.UserId,
                UserEmail = me.Email,
                LeagueId = leagueId,
                CorrelationId = req.FunctionContext.InvocationId.ToString()
            };

            var result = await _umpireService.UpdateUmpireAsync(umpireUserId, body, context);

            return ApiResponses.Ok(req, result);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateUmpire failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }

    [Function("DeactivateUmpire")]
    public async Task<HttpResponseData> DeactivateUmpire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "umpires/{umpireUserId}")] HttpRequestData req,
        string umpireUserId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);

            var reassignGames = ApiGuards.GetQueryParam(req, "reassignGames") == "true";

            var context = new CorrelationContext
            {
                UserId = me.UserId,
                UserEmail = me.Email,
                LeagueId = leagueId,
                CorrelationId = req.FunctionContext.InvocationId.ToString()
            };

            await _umpireService.DeactivateUmpireAsync(umpireUserId, reassignGames, context);

            return ApiResponses.Ok(req, new { message = "Umpire deactivated", umpireUserId, reassignedGames = reassignGames });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeactivateUmpire failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error");
        }
    }
}
