using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// League-scoped membership administration.
/// Membership rows live in GameSwapMemberships with PK=userId, RK=leagueId.
/// </summary>
public class MembershipsFunctions
{
    private readonly IMembershipRepository _membershipRepo;
    private readonly TableServiceClient _tableService; // Still needed for league existence check
    private readonly ILogger _log;

    public MembershipsFunctions(
        IMembershipRepository membershipRepo,
        TableServiceClient tableService,
        ILoggerFactory lf)
    {
        _membershipRepo = membershipRepo;
        _tableService = tableService;
        _log = lf.CreateLogger<MembershipsFunctions>();
    }

    public record CoachTeam(string? division, string? teamId);
    public record PatchMembershipReq(CoachTeam? team);
    public record CreateMembershipReq(string? userId, string? email, string? leagueId, string? role, CoachTeam? team);
    public record MembershipDto(string userId, string email, string role, CoachTeam? team);
    public record MembershipAdminDto(string userId, string email, string leagueId, string role, CoachTeam? team);

    [Function("ListMemberships")]
    [OpenApiOperation(operationId: "ListMemberships", tags: new[] { "Memberships" }, Summary = "List memberships", Description = "Retrieves memberships for a league (league admin) or all memberships (global admin with all=true). Supports filtering by role, leagueId, and search.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "all", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Set to 'true' to list all memberships across all leagues (global admin only)")]
    [OpenApiParameter(name: "leagueId", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by league ID (only with all=true)")]
    [OpenApiParameter(name: "role", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by role (Coach, Viewer, LeagueAdmin)")]
    [OpenApiParameter(name: "search", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Search by userId, email, leagueId, or role")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Memberships retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins or global admins can list memberships")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            var all = ApiGuards.GetQueryParam(req, "all");
            var isAll = all is not null && new[] { "1", "true", "yes" }.Contains(all.Trim().ToLowerInvariant());

            if (isAll)
            {
                // Authorization - global admin only for all=true
                if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only global admins can list all memberships");
                }
            }
            else
            {
                // Authorization - league admin required
                var leagueId = ApiGuards.RequireLeagueId(req);
                if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
                {
                    var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                    var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                    if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                    {
                        return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                            "Only league admins can list memberships");
                    }
                }
            }

            if (!isAll)
            {
                // List memberships for specific league
                var leagueId = ApiGuards.RequireLeagueId(req);
                var entities = await _membershipRepo.QueryAllMembershipsAsync(leagueId);
                var list = new List<MembershipDto>();

                foreach (var e in entities)
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

            // List all memberships (global admin)
            var leagueFilter = (ApiGuards.GetQueryParam(req, "leagueId") ?? "").Trim();
            var roleFilter = (ApiGuards.GetQueryParam(req, "role") ?? "").Trim();
            var search = (ApiGuards.GetQueryParam(req, "search") ?? "").Trim();

            var entities2 = await _membershipRepo.QueryAllMembershipsAsync(
                string.IsNullOrWhiteSpace(leagueFilter) ? null : leagueFilter);

            var listAll = new List<MembershipAdminDto>();

            foreach (var e in entities2)
            {
                var role = (e.GetString("Role") ?? "").Trim();
                var email = (e.GetString("Email") ?? "").Trim();
                var division = (e.GetString("Division") ?? "").Trim();
                var teamId = (e.GetString("TeamId") ?? "").Trim();
                var rowLeagueId = (e.RowKey ?? "").Trim();

                CoachTeam? team = null;
                if (string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(division)
                    && !string.IsNullOrWhiteSpace(teamId))
                    team = new CoachTeam(division, teamId);

                listAll.Add(new MembershipAdminDto(
                    userId: e.PartitionKey,
                    email: email,
                    leagueId: rowLeagueId,
                    role: role,
                    team: team));
            }

            if (!string.IsNullOrWhiteSpace(roleFilter))
            {
                listAll = listAll
                    .Where(x => string.Equals(x.role, roleFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                listAll = listAll.Where(x =>
                        x.userId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        x.email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        x.leagueId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        x.role.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var orderedAll = listAll
                .OrderBy(x => x.leagueId)
                .ThenBy(x => x.role)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.email) ? x.userId : x.email);

            return ApiResponses.Ok(req, orderedAll.ToList());
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListMemberships failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("CreateMembership")]
    [OpenApiOperation(operationId: "CreateMembership", tags: new[] { "Memberships" }, Summary = "Create membership", Description = "Creates a new membership for a user in a league. Only global admins can create memberships. Coaches must have team assignment.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreateMembershipReq), Required = true, Description = "Membership creation request with userId, email, leagueId, role, and optional team (for coaches)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(MembershipDto), Description = "Membership created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request body or missing required fields")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only global admins can create memberships")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "League not found")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/memberships")] HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can create memberships");
            }

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

            // Verify league exists
            var leagues = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
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

            var entity = new TableEntity(userId, leagueId)
            {
                ["Role"] = normalizedRole,
                ["Email"] = email,
                ["Division"] = division,
                ["TeamId"] = teamId,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            await _membershipRepo.UpsertMembershipAsync(entity);
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
    [OpenApiOperation(operationId: "PatchMembership", tags: new[] { "Memberships" }, Summary = "Update membership", Description = "Updates a coach's team assignment. Only league admins can patch memberships. Only coach memberships can be assigned to teams.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "userId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "User ID of the membership to update")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(PatchMembershipReq), Required = true, Description = "Team assignment update (set team to null to remove assignment)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(MembershipDto), Description = "Membership updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request body or membership is not a coach")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins can patch memberships")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Membership not found")]
    public async Task<HttpResponseData> Patch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "memberships/{userId}")] HttpRequestData req,
        string userId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - league admin required
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var myRole = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(myRole, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can patch memberships");
                }
            }

            userId = (userId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("userId", userId);

            var mem = await _membershipRepo.GetMembershipAsync(userId, leagueId);
            if (mem is null)
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
            await _membershipRepo.UpdateMembershipAsync(mem);

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
