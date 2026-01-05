using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// League-scoped membership administration.
/// Membership rows live in GameSwapMemberships with PK=userId, RK=leagueId.
/// </summary>
public class MembershipsFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public MembershipsFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<MembershipsFunctions>();
    }

    public record CoachTeam(string? division, string? teamId);
    public record PatchMembershipReq(CoachTeam? team);
    public record CreateMembershipReq(string? userId, string? email, string? leagueId, string? role, CoachTeam? team);
    public record MembershipDto(string userId, string email, string role, CoachTeam? team);

    [Function("ListMemberships")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Memberships);
            // Memberships table uses PK=userId, RK=leagueId. Query by RowKey (leagueId) across partitions.
            var filter = $"RowKey eq '{ApiGuards.EscapeOData(leagueId)}'";
            var list = new List<MembershipDto>();

            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var role = (e.GetString("Role") ?? "").Trim();
                var email = (e.GetString("Email") ?? "").Trim();
                var division = (e.GetString("Division") ?? "").Trim();
                var teamId = (e.GetString("TeamId") ?? "").Trim();

                CoachTeam? team = null;
                if (string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(division)
                    && !string.IsNullOrWhiteSpace(teamId))
                    team = new CoachTeam(division, teamId);

                list.Add(new MembershipDto(
                    userId: e.PartitionKey,
                    email: email,
                    role: role,
                    team: team));
            }

            // stable-ish ordering for admin UX
            var ordered = list
                .OrderBy(x => x.role)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.email) ? x.userId : x.email);

            return ApiResponses.Ok(req, ordered.ToList());
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListMemberships failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("CreateMembership")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/memberships")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            var body = await HttpUtil.ReadJsonAsync<CreateMembershipReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var userId = (body.userId ?? "").Trim();
            var email = (body.email ?? "").Trim();
            var leagueId = (body.leagueId ?? "").Trim();
            var role = (body.role ?? "").Trim();

            if (string.IsNullOrWhiteSpace(userId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "userId is required");
            if (string.IsNullOrWhiteSpace(leagueId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "leagueId is required");
            if (string.IsNullOrWhiteSpace(role))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "role is required");

            ApiGuards.EnsureValidTableKeyPart("userId", userId);
            ApiGuards.EnsureValidTableKeyPart("leagueId", leagueId);

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
                _ = (await leagues.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, leagueId)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
            }

            string division = "";
            string teamId = "";
            if (string.Equals(normalizedRole, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) && body.team is not null)
            {
                division = (body.team.division ?? "").Trim();
                teamId = (body.team.teamId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(division))
                    ApiGuards.EnsureValidTableKeyPart("division", division);
                if (!string.IsNullOrWhiteSpace(teamId))
                    ApiGuards.EnsureValidTableKeyPart("teamId", teamId);
            }

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Memberships);
            var entity = new TableEntity(userId, leagueId)
            {
                ["Role"] = normalizedRole,
                ["Email"] = email,
                ["Division"] = division,
                ["TeamId"] = teamId,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            await table.UpsertEntityAsync(entity, TableUpdateMode.Merge);
            return ApiResponses.Ok(req, new MembershipDto(userId, email, normalizedRole,
                string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId)
                    ? null
                    : new CoachTeam(division, teamId)));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateMembership failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("PatchMembership")]
    public async Task<HttpResponseData> Patch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "memberships/{userId}")] HttpRequestData req,
        string userId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            userId = (userId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Memberships);
            TableEntity mem;
            try
            {
                mem = (await table.GetEntityAsync<TableEntity>(userId, leagueId)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "membership not found");
            }

            var role = (mem.GetString("Role") ?? "").Trim();
            if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Only Coach memberships can be assigned to a team.");

            var body = await HttpUtil.ReadJsonAsync<PatchMembershipReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            if (body.team is null)
            {
                mem["Division"] = "";
                mem["TeamId"] = "";
            }
            else
            {
                var division = (body.team.division ?? "").Trim();
                var teamId = (body.team.teamId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "team.division and team.teamId are required");
                ApiGuards.EnsureValidTableKeyPart("division", division);
                ApiGuards.EnsureValidTableKeyPart("teamId", teamId);

                mem["Division"] = division;
                mem["TeamId"] = teamId;
            }

            mem["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await table.UpdateEntityAsync(mem, mem.ETag, TableUpdateMode.Replace);

            // Return normalized dto
            var outDivision = (mem.GetString("Division") ?? "").Trim();
            var outTeamId = (mem.GetString("TeamId") ?? "").Trim();
            var email = (mem.GetString("Email") ?? "").Trim();

            CoachTeam? team = (string.IsNullOrWhiteSpace(outDivision) || string.IsNullOrWhiteSpace(outTeamId))
                ? null
                : new CoachTeam(outDivision, outTeamId);

            return ApiResponses.Ok(req, new MembershipDto(userId, email, role, team));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "PatchMembership failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
