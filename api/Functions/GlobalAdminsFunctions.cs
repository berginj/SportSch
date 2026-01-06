using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class GlobalAdminsFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    private const string TableName = "GameSwapGlobalAdmins";

    public GlobalAdminsFunctions(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<GlobalAdminsFunctions>();
        _svc = tableServiceClient;
    }

    public record GlobalAdminDto(string userId, string email, DateTimeOffset createdUtc);
    public record UpsertReq(string? userId, string? email);

    private static GlobalAdminDto ToDto(TableEntity e) =>
        new(
            userId: e.RowKey,
            email: e.GetString("Email") ?? "",
            createdUtc: e.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.MinValue
        );

    [Function("ListGlobalAdmins")]
    public Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/globaladmins")] HttpRequestData req)
        => ListCore(req);

    [Function("ListGlobalAdmins_Alt")]
    public Task<HttpResponseData> ListAlt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "globaladmins")] HttpRequestData req)
        => ListCore(req);

    [Function("UpsertGlobalAdmin")]
    public Task<HttpResponseData> Upsert(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/globaladmins")] HttpRequestData req)
        => UpsertCore(req);

    [Function("UpsertGlobalAdmin_Alt")]
    public Task<HttpResponseData> UpsertAlt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "globaladmins")] HttpRequestData req)
        => UpsertCore(req);

    [Function("DeleteGlobalAdmin")]
    public Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "admin/globaladmins/{userId}")] HttpRequestData req,
        string userId)
        => DeleteCore(req, userId);

    [Function("DeleteGlobalAdmin_Alt")]
    public Task<HttpResponseData> DeleteAlt(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "globaladmins/{userId}")] HttpRequestData req,
        string userId)
        => DeleteCore(req, userId);

    private async Task<HttpResponseData> ListCore(HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var table = await TableClients.GetTableAsync(_svc, TableName);
            var list = new List<GlobalAdminDto>();
            await foreach (var e in table.QueryAsync<TableEntity>(x => x.PartitionKey == "GLOBAL"))
                list.Add(ToDto(e));

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(list.OrderBy(x => x.email).ThenBy(x => x.userId));
            return res;
        }
        catch (ApiGuards.HttpError ex)
        {
            return await Err(req, ex.Message, (HttpStatusCode)ex.Status);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListGlobalAdmins failed");
            return await Err(req, "Internal Server Error", HttpStatusCode.InternalServerError);
        }
    }

    private async Task<HttpResponseData> UpsertCore(HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            UpsertReq? body;
            try { body = await req.ReadFromJsonAsync<UpsertReq>(); }
            catch { return await Err(req, "Invalid JSON body", HttpStatusCode.BadRequest); }

            var userId = (body?.userId ?? "").Trim();
            var email = (body?.email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(userId))
                return await Err(req, "userId is required", HttpStatusCode.BadRequest);
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var table = await TableClients.GetTableAsync(_svc, TableName);
            var now = DateTimeOffset.UtcNow;

            var entity = new TableEntity("GLOBAL", userId)
            {
                ["UserId"] = userId,
                ["Email"] = email,
                ["CreatedUtc"] = now,
                ["UpdatedUtc"] = now,
                ["CreatedBy"] = me.UserId
            };

            await table.UpsertEntityAsync(entity, TableUpdateMode.Merge);

            await AuditLog.WriteAsync(
                _svc,
                actorUserId: me.UserId,
                actorEmail: me.Email,
                action: "GlobalAdmin.Granted",
                targetType: "GlobalAdmin",
                targetId: userId,
                leagueId: null,
                result: "Granted",
                targetUserId: userId,
                targetEmail: email,
                details: null
            );

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(ToDto(entity));
            return res;
        }
        catch (ApiGuards.HttpError ex)
        {
            return await Err(req, ex.Message, (HttpStatusCode)ex.Status);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpsertGlobalAdmin failed");
            return await Err(req, "Internal Server Error", HttpStatusCode.InternalServerError);
        }
    }

    private async Task<HttpResponseData> DeleteCore(HttpRequestData req, string userId)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            userId = (userId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(userId))
                return await Err(req, "userId is required", HttpStatusCode.BadRequest);
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var table = await TableClients.GetTableAsync(_svc, TableName);
            string? email = null;

            try
            {
                var existing = (await table.GetEntityAsync<TableEntity>("GLOBAL", userId)).Value;
                email = existing.GetString("Email");
                await table.DeleteEntityAsync("GLOBAL", userId);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // idempotent
            }

            await AuditLog.WriteAsync(
                _svc,
                actorUserId: me.UserId,
                actorEmail: me.Email,
                action: "GlobalAdmin.Revoked",
                targetType: "GlobalAdmin",
                targetId: userId,
                leagueId: null,
                result: "Revoked",
                targetUserId: userId,
                targetEmail: email,
                details: null
            );

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { ok = true });
            return res;
        }
        catch (ApiGuards.HttpError ex)
        {
            return await Err(req, ex.Message, (HttpStatusCode)ex.Status);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeleteGlobalAdmin failed");
            return await Err(req, "Internal Server Error", HttpStatusCode.InternalServerError);
        }
    }

    private static async Task<HttpResponseData> Err(HttpRequestData req, string msg, HttpStatusCode code)
    {
        var res = req.CreateResponse(code);
        await res.WriteAsJsonAsync(new { error = msg });
        return res;
    }
}
