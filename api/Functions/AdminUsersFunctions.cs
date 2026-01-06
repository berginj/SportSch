using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class AdminUsersFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public AdminUsersFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<AdminUsersFunctions>();
    }

    public record UserProfileDto(
        string userId,
        string email,
        string homeLeagueId,
        string? homeLeagueRole,
        DateTimeOffset updatedUtc
    );

    public record UpsertUserReq(string? userId, string? email, string? homeLeagueId, string? role);

    [Function("ListUsers_Admin")]
    public Task<HttpResponseData> ListAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/users")] HttpRequestData req)
        => ListCore(req);

    [Function("ListUsers_Alt")]
    public Task<HttpResponseData> ListAlt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users")] HttpRequestData req)
        => ListCore(req);

    [Function("UpsertUser_Admin")]
    public Task<HttpResponseData> UpsertAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/users")] HttpRequestData req)
        => UpsertCore(req);

    [Function("UpsertUser_Alt")]
    public Task<HttpResponseData> UpsertAlt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users")] HttpRequestData req)
        => UpsertCore(req);

    private async Task<HttpResponseData> ListCore(HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var search = (ApiGuards.GetQueryParam(req, "search") ?? "").Trim();
            var usersTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Users);
            var memTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Memberships);

            var list = new List<UserProfileDto>();
            await foreach (var e in usersTable.QueryAsync<TableEntity>(x => x.PartitionKey == Constants.Pk.Users))
            {
                var userId = (e.RowKey ?? "").Trim();
                var email = (e.GetString("Email") ?? "").Trim();
                var homeLeagueId = (e.GetString("HomeLeagueId") ?? "").Trim();
                var updatedUtc = e.GetDateTimeOffset("UpdatedUtc") ?? DateTimeOffset.MinValue;

                string? homeRole = null;
                if (!string.IsNullOrWhiteSpace(homeLeagueId))
                {
                    try
                    {
                        var mem = (await memTable.GetEntityAsync<TableEntity>(userId, homeLeagueId)).Value;
                        homeRole = (mem.GetString("Role") ?? "").Trim();
                    }
                    catch (RequestFailedException ex) when (ex.Status == 404)
                    {
                        homeRole = null;
                    }
                }

                list.Add(new UserProfileDto(userId, email, homeLeagueId, homeRole, updatedUtc));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                list = list.Where(u =>
                        u.userId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        u.email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        u.homeLeagueId.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var ordered = list
                .OrderBy(x => string.IsNullOrWhiteSpace(x.email) ? x.userId : x.email)
                .ThenBy(x => x.userId);

            return ApiResponses.Ok(req, ordered.ToList());
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListUsers failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<HttpResponseData> UpsertCore(HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var body = await HttpUtil.ReadJsonAsync<UpsertUserReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var userId = (body.userId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(userId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "userId is required");
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var email = (body.email ?? "").Trim();
            var homeLeagueId = (body.homeLeagueId ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(homeLeagueId))
                ApiGuards.EnsureValidTableKeyPart("homeLeagueId", homeLeagueId);

            var usersTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Users);
            var now = DateTimeOffset.UtcNow;
            var userEntity = new TableEntity(Constants.Pk.Users, userId)
            {
                ["UserId"] = userId,
                ["Email"] = email,
                ["HomeLeagueId"] = homeLeagueId,
                ["UpdatedUtc"] = now
            };

            await usersTable.UpsertEntityAsync(userEntity, TableUpdateMode.Merge);

            var role = (body.role ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(role))
            {
                if (string.IsNullOrWhiteSpace(homeLeagueId))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "homeLeagueId is required when role is provided");

                var normalizedRole = role switch
                {
                    var r when r.Equals(Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) => Constants.Roles.Coach,
                    var r when r.Equals(Constants.Roles.Viewer, StringComparison.OrdinalIgnoreCase) => Constants.Roles.Viewer,
                    var r when r.Equals(Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase) => Constants.Roles.LeagueAdmin,
                    _ => ""
                };

                if (string.IsNullOrWhiteSpace(normalizedRole))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "role must be Coach, Viewer, or LeagueAdmin");

                var leagues = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
                try
                {
                    _ = (await leagues.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, homeLeagueId)).Value;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {homeLeagueId}");
                }

                var memTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Memberships);
                var mem = new TableEntity(userId, homeLeagueId)
                {
                    ["Role"] = normalizedRole,
                    ["Email"] = email,
                    ["UpdatedUtc"] = now
                };

                await memTable.UpsertEntityAsync(mem, TableUpdateMode.Merge);
            }

            string? homeRole = null;
            if (!string.IsNullOrWhiteSpace(homeLeagueId))
            {
                try
                {
                    var memTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Memberships);
                    var mem = (await memTable.GetEntityAsync<TableEntity>(userId, homeLeagueId)).Value;
                    homeRole = (mem.GetString("Role") ?? "").Trim();
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    homeRole = null;
                }
            }

            return ApiResponses.Ok(req, new UserProfileDto(userId, email, homeLeagueId, homeRole, now));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpsertUser failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
