using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Functions;

public class LeagueInvitesFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    private const string InvitesTableName = "GameSwapLeagueInvites";
    private const string MembershipsTableName = "GameSwapMemberships";

    public LeagueInvitesFunctions(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<LeagueInvitesFunctions>();
        _svc = tableServiceClient;
    }

    public record CreateInviteReq(string? leagueId, string? inviteEmail, string? role, int? expiresHours);
    public record InviteDto(string leagueId, string inviteId, string inviteEmail, string role, string status, DateTimeOffset expiresUtc);

    [Function("CreateInvite_Admin")]
    public async Task<HttpResponseData> CreateAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/invites")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var body = await HttpUtil.ReadJsonAsync<CreateInviteReq>(req);
            if (body is null) return HttpUtil.Json(req, HttpStatusCode.BadRequest, new { error = "Invalid JSON body" });

            var leagueId = (body.leagueId ?? "").Trim();
            var email = (body.inviteEmail ?? "").Trim();
            var role = string.IsNullOrWhiteSpace(body.role) ? "LeagueAdmin" : body.role!.Trim();

            if (string.IsNullOrWhiteSpace(leagueId)) return HttpUtil.Json(req, HttpStatusCode.BadRequest, new { error = "leagueId is required" });
            if (string.IsNullOrWhiteSpace(email)) return HttpUtil.Json(req, HttpStatusCode.BadRequest, new { error = "inviteEmail is required" });

            var inviteId = Guid.NewGuid().ToString("N");
            var expires = DateTimeOffset.UtcNow.AddHours(body.expiresHours ?? 168); // default 7 days

            var pk = $"LEAGUEINVITE#{leagueId}";
            var entity = new TableEntity(pk, inviteId)
            {
                ["LeagueId"] = leagueId,
                ["InviteEmail"] = email,
                ["Role"] = role,
                ["Status"] = "Sent",
                ["ExpiresUtc"] = expires,
                ["CreatedUtc"] = DateTimeOffset.UtcNow
            };

            var table = await TableClients.GetTableAsync(_svc, InvitesTableName);
            await table.AddEntityAsync(entity);

            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(ToDto(entity));
            return res;
        }
        catch (UnauthorizedAccessException ua)
        {
            return HttpUtil.Text(req, HttpStatusCode.Forbidden, ua.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateInvite_Admin failed");
            return HttpUtil.Text(req, HttpStatusCode.InternalServerError, "Internal Server Error");
        }
    }

    public record AcceptInviteReq(string? leagueId, string? inviteId);

    [Function("AcceptInvite")]
    public async Task<HttpResponseData> Accept(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "invites/accept")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);

            var body = await HttpUtil.ReadJsonAsync<AcceptInviteReq>(req);
            if (body is null) return HttpUtil.Json(req, HttpStatusCode.BadRequest, new { error = "Invalid JSON body" });

            var leagueId = (body.leagueId ?? "").Trim();
            var inviteId = (body.inviteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(inviteId))
                return HttpUtil.Json(req, HttpStatusCode.BadRequest, new { error = "leagueId and inviteId are required" });

            var pk = $"LEAGUEINVITE#{leagueId}";
            var table = await TableClients.GetTableAsync(_svc, InvitesTableName);
            TableEntity e;
            try { e = (await table.GetEntityAsync<TableEntity>(pk, inviteId)).Value; }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return HttpUtil.Json(req, HttpStatusCode.NotFound, new { error = "invite not found" });
            }

            var status = e.GetString("Status") ?? "Sent";
            var expires = e.GetDateTimeOffset("ExpiresUtc") ?? DateTimeOffset.MinValue;

            if (!string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase))
                return HttpUtil.Json(req, HttpStatusCode.Conflict, new { error = $"invite not in Sent state (current: {status})" });

            if (expires < DateTimeOffset.UtcNow)
            {
                e["Status"] = "Expired";
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);
                return HttpUtil.Json(req, HttpStatusCode.Gone, new { error = "invite expired" });
            }

            // Soft validation: if we know caller email, ensure it matches the invite email.
            var inviteEmail = (e.GetString("InviteEmail") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(inviteEmail) &&
                !string.IsNullOrWhiteSpace(me.Email) &&
                !string.Equals(inviteEmail, me.Email, StringComparison.OrdinalIgnoreCase))
            {
                return HttpUtil.Text(req, HttpStatusCode.Forbidden, "Invite email does not match caller.");
            }

            e["Status"] = "Accepted";
            e["AcceptedBy"] = me.Email;
            e["AcceptedUserId"] = me.UserId;
            e["AcceptedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);

            // Bootstrap membership row if we have a caller userId
            if (!string.IsNullOrWhiteSpace(me.UserId) && !me.UserId.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                var memberships = await TableClients.GetTableAsync(_svc, MembershipsTableName);
                var role = (e.GetString("Role") ?? "LeagueAdmin").Trim();

                var mem = new TableEntity(me.UserId, leagueId)
                {
                    ["UserId"] = me.UserId,
                    ["LeagueId"] = leagueId,
                    ["Role"] = role,
                    ["Email"] = me.Email,
                    ["CreatedUtc"] = DateTimeOffset.UtcNow
                };

                await memberships.UpsertEntityAsync(mem, TableUpdateMode.Merge);
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(ToDto(e));
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AcceptInvite failed");
            return HttpUtil.Text(req, HttpStatusCode.InternalServerError, "Internal Server Error");
        }
    }

    private static InviteDto ToDto(TableEntity e) =>
        new(
            leagueId: e.GetString("LeagueId") ?? "",
            inviteId: e.RowKey,
            inviteEmail: e.GetString("InviteEmail") ?? "",
            role: e.GetString("Role") ?? "LeagueAdmin",
            status: e.GetString("Status") ?? "Sent",
            expiresUtc: e.GetDateTimeOffset("ExpiresUtc") ?? DateTimeOffset.MinValue
        );
}
