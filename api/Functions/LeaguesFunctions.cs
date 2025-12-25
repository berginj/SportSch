using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class LeaguesFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public LeaguesFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<LeaguesFunctions>();
    }

    public record LeagueContact(string? name, string? email, string? phone);
    public record LeagueDto(string leagueId, string name, string timezone, string status, LeagueContact contact);
    public record CreateLeagueReq(string? leagueId, string? name, string? timezone);
    public record PatchLeagueReq(string? name, string? timezone, string? status, LeagueContact? contact);

    private static LeagueDto ToDto(TableEntity e)
    {
        return new LeagueDto(
            leagueId: e.RowKey,
            name: (e.GetString("Name") ?? e.RowKey).Trim(),
            timezone: (e.GetString("Timezone") ?? "America/New_York").Trim(),
            status: (e.GetString("Status") ?? "Active").Trim(),
            contact: new LeagueContact(
                name: (e.GetString("ContactName") ?? "").Trim(),
                email: (e.GetString("ContactEmail") ?? "").Trim(),
                phone: (e.GetString("ContactPhone") ?? "").Trim()
            )
        );
    }

    [Function("ListLeagues")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "leagues")] HttpRequestData req)
    {
        try
        {
            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
            var list = new List<LeagueDto>();
            await foreach (var e in table.QueryAsync<TableEntity>(x => x.PartitionKey == Constants.Pk.Leagues))
            {
                var status = (e.GetString("Status") ?? "Active").Trim();
                if (string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Deleted", StringComparison.OrdinalIgnoreCase))
                    continue;

                list.Add(ToDto(e));
            }

            return ApiResponses.Ok(req, list.OrderBy(x => x.name).ThenBy(x => x.leagueId));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListLeagues failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("GetLeague")]
    public async Task<HttpResponseData> GetLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "league")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
            try
            {
                var e = (await table.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
                return ApiResponses.Ok(req, ToDto(e));
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
            }
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetLeague failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("PatchLeague")]
    public async Task<HttpResponseData> PatchLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "league")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<PatchLeagueReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
            TableEntity e;
            try
            {
                e = (await table.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
            }

            if (!string.IsNullOrWhiteSpace(body.name)) e["Name"] = body.name!.Trim();
            if (!string.IsNullOrWhiteSpace(body.timezone)) e["Timezone"] = body.timezone!.Trim();
            if (!string.IsNullOrWhiteSpace(body.status)) e["Status"] = body.status!.Trim();

            if (body.contact is not null)
            {
                e["ContactName"] = (body.contact.name ?? "").Trim();
                e["ContactEmail"] = (body.contact.email ?? "").Trim();
                e["ContactPhone"] = (body.contact.phone ?? "").Trim();
            }

            e["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);

            return ApiResponses.Ok(req, ToDto(e));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PatchLeague failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ListLeagues_Admin")]
    public async Task<HttpResponseData> ListAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/leagues")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
            var list = new List<LeagueDto>();
            await foreach (var e in table.QueryAsync<TableEntity>(x => x.PartitionKey == Constants.Pk.Leagues))
                list.Add(ToDto(e));

            return ApiResponses.Ok(req, list.OrderBy(x => x.leagueId));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListLeagues_Admin failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ListLeagues_Global")]
    public async Task<HttpResponseData> ListGlobal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "global/leagues")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
            var list = new List<LeagueDto>();
            await foreach (var e in table.QueryAsync<TableEntity>(x => x.PartitionKey == Constants.Pk.Leagues))
                list.Add(ToDto(e));

            return ApiResponses.Ok(req, list.OrderBy(x => x.leagueId));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListLeagues_Global failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("CreateLeague_Admin")]
    public async Task<HttpResponseData> CreateAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/leagues")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var body = await HttpUtil.ReadJsonAsync<CreateLeagueReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var leagueId = (body.leagueId ?? "").Trim();
            var name = (body.name ?? "").Trim();
            var timezone = string.IsNullOrWhiteSpace(body.timezone) ? "America/New_York" : body.timezone!.Trim();

            if (string.IsNullOrWhiteSpace(leagueId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "leagueId is required");
            if (string.IsNullOrWhiteSpace(name))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "name is required");
            ApiGuards.EnsureValidTableKeyPart("leagueId", leagueId);

            var now = DateTimeOffset.UtcNow;
            var e = new TableEntity(Constants.Pk.Leagues, leagueId)
            {
                ["LeagueId"] = leagueId,
                ["Name"] = name,
                ["Timezone"] = timezone,
                ["Status"] = "Active",
                ["ContactName"] = "",
                ["ContactEmail"] = "",
                ["ContactPhone"] = "",
                ["CreatedUtc"] = now,
                ["UpdatedUtc"] = now
            };

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
            try
            {
                await table.AddEntityAsync(e);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", $"league already exists: {leagueId}");
            }

            return ApiResponses.Ok(req, ToDto(e), HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateLeague_Admin failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("CreateLeague_Global")]
    public async Task<HttpResponseData> CreateGlobal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "global/leagues")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var body = await HttpUtil.ReadJsonAsync<CreateLeagueReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var leagueId = (body.leagueId ?? "").Trim();
            var name = (body.name ?? "").Trim();
            var timezone = string.IsNullOrWhiteSpace(body.timezone) ? "America/New_York" : body.timezone!.Trim();

            if (string.IsNullOrWhiteSpace(leagueId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "leagueId is required");
            if (string.IsNullOrWhiteSpace(name))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "name is required");
            ApiGuards.EnsureValidTableKeyPart("leagueId", leagueId);

            var now = DateTimeOffset.UtcNow;
            var e = new TableEntity(Constants.Pk.Leagues, leagueId)
            {
                ["LeagueId"] = leagueId,
                ["Name"] = name,
                ["Timezone"] = timezone,
                ["Status"] = "Active",
                ["ContactName"] = "",
                ["ContactEmail"] = "",
                ["ContactPhone"] = "",
                ["CreatedUtc"] = now,
                ["UpdatedUtc"] = now
            };

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
            try
            {
                await table.AddEntityAsync(e);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", $"league already exists: {leagueId}");
            }

            return ApiResponses.Ok(req, ToDto(e), HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateLeague_Global failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
