using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class AccessRequestsFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public AccessRequestsFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<AccessRequestsFunctions>();
    }

    public record CreateAccessRequestReq(string? requestedRole, string? notes);
    public record ApproveAccessReq(string? role, CoachTeam? team);
    public record DenyAccessReq(string? reason);
    public record CoachTeam(string? division, string? teamId);

    public record AccessRequestDto(
        string leagueId,
        string userId,
        string email,
        string requestedRole,
        string status,
        string notes,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc);

    private static string ReqPk(string leagueId) => Constants.Pk.AccessRequests(leagueId);
    private static string ReqRk(string userId) => userId; // one request per (league,user)

    private const string AccessReqPkPrefix = "ACCESSREQ|";
    private const string NewLeagueId = "NEW_LEAGUE";

    private static string LeagueIdFromPk(string pk)
    {
        if (string.IsNullOrWhiteSpace(pk)) return "";
        if (!pk.StartsWith(AccessReqPkPrefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[AccessReqPkPrefix.Length..];
    }

    private static AccessRequestDto ToDto(TableEntity e)
        => new(
            leagueId: (e.GetString("LeagueId") ?? "").Trim(),
            userId: (e.GetString("UserId") ?? e.RowKey).Trim(),
            email: (e.GetString("Email") ?? "").Trim(),
            requestedRole: (e.GetString("RequestedRole") ?? "").Trim(),
            status: (e.GetString("Status") ?? Constants.Status.AccessRequestPending).Trim(),
            notes: (e.GetString("Notes") ?? "").Trim(),
            createdUtc: e.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.MinValue,
            updatedUtc: e.GetDateTimeOffset("UpdatedUtc") ?? DateTimeOffset.MinValue
        );

    private static AccessRequestDto ToDtoWithFallback(TableEntity e)
    {
        var dto = ToDto(e);
        if (!string.IsNullOrWhiteSpace(dto.leagueId)) return dto;
        return dto with { leagueId = LeagueIdFromPk(e.PartitionKey) };
    }

    private static bool IsValidRequestedRole(string role)
        => string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, Constants.Roles.Viewer, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    [Function("CreateAccessRequest")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "accessrequests")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            var body = await HttpUtil.ReadJsonAsync<CreateAccessRequestReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var requestedRole = (body.requestedRole ?? "").Trim();
            var notes = (body.notes ?? "").Trim();

            if (string.IsNullOrWhiteSpace(requestedRole))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "requestedRole is required");

            if (!IsValidRequestedRole(requestedRole))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "requestedRole must be Coach, Viewer, or LeagueAdmin");

            if (!string.Equals(leagueId, NewLeagueId, StringComparison.OrdinalIgnoreCase))
            {
                // Ensure league exists
                var leagues = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
                try
                {
                    _ = (await leagues.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
                }
            }

            var requests = await TableClients.GetTableAsync(_svc, Constants.Tables.AccessRequests);
            var pk = ReqPk(leagueId);
            var rk = ReqRk(me.UserId);
            var now = DateTimeOffset.UtcNow;

            // Preserve CreatedUtc if existing
            DateTimeOffset createdUtc = now;
            try
            {
                var existing = (await requests.GetEntityAsync<TableEntity>(pk, rk)).Value;
                createdUtc = existing.GetDateTimeOffset("CreatedUtc") ?? now;

                var existingStatus = (existing.GetString("Status") ?? Constants.Status.AccessRequestPending).Trim();
                if (string.Equals(existingStatus, Constants.Status.AccessRequestApproved, StringComparison.OrdinalIgnoreCase))
                    return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "Access already approved for this league.");
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // ok
            }

            var entity = new TableEntity(pk, rk)
            {
                ["LeagueId"] = leagueId,
                ["UserId"] = me.UserId,
                ["Email"] = me.Email,
                ["RequestedRole"] = requestedRole,
                ["Status"] = Constants.Status.AccessRequestPending,
                ["Notes"] = notes,
                ["CreatedUtc"] = createdUtc,
                ["UpdatedUtc"] = now
            };

            await requests.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            return ApiResponses.Ok(req, ToDto(entity));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateAccessRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ListMyAccessRequests")]
    public async Task<HttpResponseData> ListMine(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "accessrequests/mine")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.AccessRequests);
            var filter = $"UserId eq '{ApiGuards.EscapeOData(me.UserId)}'";
            var list = new List<AccessRequestDto>();
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
                list.Add(ToDtoWithFallback(e));

            return ApiResponses.Ok(req, list.OrderByDescending(x => x.updatedUtc));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListMyAccessRequests failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ListAccessRequests")]
    public async Task<HttpResponseData> ListForLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "accessrequests")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            var status = (ApiGuards.GetQueryParam(req, "status") ?? Constants.Status.AccessRequestPending).Trim();
            var all = IsTruthy(ApiGuards.GetQueryParam(req, "all"));

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.AccessRequests);
            var list = new List<AccessRequestDto>();

            if (all)
            {
                await ApiGuards.RequireGlobalAdminAsync(_svc, me);
                var filter = $"Status eq '{ApiGuards.EscapeOData(status)}'";
                await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
                    list.Add(ToDtoWithFallback(e));

                return ApiResponses.Ok(req, list.OrderBy(x => x.leagueId).ThenBy(x => x.email));
            }

            var leagueId = ApiGuards.RequireLeagueId(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var leagueFilter = $"PartitionKey eq '{ApiGuards.EscapeOData(ReqPk(leagueId))}' and Status eq '{ApiGuards.EscapeOData(status)}'";
            await foreach (var e in table.QueryAsync<TableEntity>(filter: leagueFilter))
                list.Add(ToDtoWithFallback(e));

            return ApiResponses.Ok(req, list.OrderBy(x => x.email));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListAccessRequests failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ApproveAccessRequest")]
    public async Task<HttpResponseData> Approve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "accessrequests/{userId}/approve")] HttpRequestData req,
        string userId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            userId = (userId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.AccessRequests);
            var pk = ReqPk(leagueId);
            var rk = ReqRk(userId);

            TableEntity ar;
            try
            {
                ar = (await table.GetEntityAsync<TableEntity>(pk, rk)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "access request not found");
            }

            var body = await HttpUtil.ReadJsonAsync<ApproveAccessReq>(req) ?? new ApproveAccessReq(null, null);

            // Default: honor requestedRole
            var requestedRole = (ar.GetString("RequestedRole") ?? Constants.Roles.Viewer).Trim();
            var role = (body.role ?? requestedRole).Trim();

            if (!IsValidRequestedRole(role))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "role must be Coach, Viewer, or LeagueAdmin");

            var division = (body.team?.division ?? "").Trim();
            var teamId = (body.team?.teamId ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(division))
                ApiGuards.EnsureValidTableKeyPart("division", division);
            if (!string.IsNullOrWhiteSpace(teamId))
                ApiGuards.EnsureValidTableKeyPart("teamId", teamId);

            // Upsert membership (PK=userId, RK=leagueId)
            var memTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Memberships);
            var mem = new TableEntity(userId, leagueId)
            {
                ["Role"] = role,
                ["Email"] = (ar.GetString("Email") ?? "").Trim(),
                ["Division"] = division,
                ["TeamId"] = teamId,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            await memTable.UpsertEntityAsync(mem, TableUpdateMode.Merge);

            // Mark request approved
            ar["Status"] = Constants.Status.AccessRequestApproved;
            ar["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await table.UpdateEntityAsync(ar, ar.ETag, TableUpdateMode.Replace);

            return ApiResponses.Ok(req, new { leagueId, userId, status = Constants.Status.AccessRequestApproved });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ApproveAccessRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("DenyAccessRequest")]
    public async Task<HttpResponseData> Deny(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "accessrequests/{userId}/deny")] HttpRequestData req,
        string userId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            userId = (userId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.AccessRequests);
            var pk = ReqPk(leagueId);
            var rk = ReqRk(userId);

            TableEntity ar;
            try
            {
                ar = (await table.GetEntityAsync<TableEntity>(pk, rk)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "access request not found");
            }

            var body = await HttpUtil.ReadJsonAsync<DenyAccessReq>(req);
            var reason = (body?.reason ?? "").Trim();

            ar["Status"] = Constants.Status.AccessRequestDenied;
            ar["DeniedReason"] = reason;
            ar["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await table.UpdateEntityAsync(ar, ar.ETag, TableUpdateMode.Replace);

            return ApiResponses.Ok(req, new { leagueId, userId, status = Constants.Status.AccessRequestDenied });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DenyAccessRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
