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

public class TeamsFunctions
{
    private readonly ITeamRepository _teamRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger _log;

    public TeamsFunctions(
        ITeamRepository teamRepo,
        IMembershipRepository membershipRepo,
        ILoggerFactory lf)
    {
        _teamRepo = teamRepo;
        _membershipRepo = membershipRepo;
        _log = lf.CreateLogger<TeamsFunctions>();
    }

    public record ContactDto(string? name, string? email, string? phone);
    public record TeamDto(
        string division,
        string teamId,
        string name,
        ContactDto primaryContact,
        List<ContactDto>? assistantCoaches,
        string? clinicPreference,
        bool onboardingComplete
    );
    public record UpsertTeamReq(
        string? division,
        string? teamId,
        string? name,
        ContactDto? primaryContact,
        List<ContactDto>? assistantCoaches,
        string? clinicPreference,
        bool? onboardingComplete
    );

    private static string TeamPk(string leagueId, string division) => $"TEAM|{leagueId}|{division}";

    private static TeamDto ToDto(TableEntity e)
    {
        // Deserialize assistant coaches from JSON array
        var assistantCoachesJson = (e.GetString("AssistantCoaches") ?? "").Trim();
        List<ContactDto>? assistantCoaches = null;
        if (!string.IsNullOrWhiteSpace(assistantCoachesJson))
        {
            try
            {
                assistantCoaches = System.Text.Json.JsonSerializer.Deserialize<List<ContactDto>>(assistantCoachesJson);
            }
            catch
            {
                assistantCoaches = new List<ContactDto>();
            }
        }

        return new TeamDto(
            division: (e.GetString("Division") ?? "").Trim(),
            teamId: e.RowKey,
            name: (e.GetString("Name") ?? "").Trim(),
            primaryContact: new ContactDto(
                name: (e.GetString("PrimaryContactName") ?? "").Trim(),
                email: (e.GetString("PrimaryContactEmail") ?? "").Trim(),
                phone: (e.GetString("PrimaryContactPhone") ?? "").Trim()
            ),
            assistantCoaches: assistantCoaches,
            clinicPreference: (e.GetString("ClinicPreference") ?? "").Trim(),
            onboardingComplete: e.GetBoolean("OnboardingComplete") ?? false
        );
    }

    [Function("GetTeams")]
    [OpenApiOperation(operationId: "GetTeams", tags: new[] { "Teams" }, Summary = "Get teams", Description = "Retrieves all teams for a league, optionally filtered by division.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter teams by division code")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Teams retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Not a member of this league")]
    public async Task<HttpResponseData> GetTeams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "teams")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be member or global admin
            if (!await _membershipRepo.IsMemberAsync(me.UserId, leagueId) &&
                !await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only league members can view teams");
            }

            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();

            var list = new List<TeamDto>();

            if (!string.IsNullOrWhiteSpace(division))
            {
                ApiGuards.EnsureValidTableKeyPart("division", division);
                var entities = await _teamRepo.QueryTeamsByDivisionAsync(leagueId, division);
                foreach (var e in entities)
                    list.Add(ToDto(e));
            }
            else
            {
                var entities = await _teamRepo.QueryAllTeamsAsync(leagueId);
                foreach (var e in entities)
                    list.Add(ToDto(e));
            }

            return ApiResponses.Ok(req, list.OrderBy(x => x.division).ThenBy(x => x.name));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetTeams failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("CreateTeam")]
    [OpenApiOperation(operationId: "CreateTeam", tags: new[] { "Teams" }, Summary = "Create team", Description = "Creates a new team in a division. Only league admins can create teams.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpsertTeamReq), Required = true, Description = "Team creation request with division, teamId, name, and primary contact")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(TeamDto), Description = "Team created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request body or missing required fields")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins can create teams")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Conflict, contentType: "application/json", bodyType: typeof(object), Description = "Team already exists")]
    public async Task<HttpResponseData> CreateTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "teams")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - league admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can create teams");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<UpsertTeamReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            var teamId = (body.teamId ?? "").Trim();
            var name = (body.name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(name))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division, teamId, and name are required");
            ApiGuards.EnsureValidTableKeyPart("division", division);
            ApiGuards.EnsureValidTableKeyPart("teamId", teamId);

            // Serialize assistant coaches to JSON
            var assistantCoachesJson = "";
            if (body.assistantCoaches is not null && body.assistantCoaches.Count > 0)
            {
                assistantCoachesJson = System.Text.Json.JsonSerializer.Serialize(body.assistantCoaches);
            }

            var e = new TableEntity(TeamPk(leagueId, division), teamId)
            {
                ["LeagueId"] = leagueId,
                ["Division"] = division,
                ["TeamId"] = teamId,
                ["Name"] = name,
                ["PrimaryContactName"] = (body.primaryContact?.name ?? "").Trim(),
                ["PrimaryContactEmail"] = (body.primaryContact?.email ?? "").Trim(),
                ["PrimaryContactPhone"] = (body.primaryContact?.phone ?? "").Trim(),
                ["AssistantCoaches"] = assistantCoachesJson,
                ["ClinicPreference"] = (body.clinicPreference ?? "").Trim(),
                ["OnboardingComplete"] = body.onboardingComplete ?? false,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            try
            {
                await _teamRepo.CreateTeamAsync(e);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "team already exists");
            }

            return ApiResponses.Ok(req, ToDto(e), HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateTeam failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("PatchTeam")]
    [OpenApiOperation(operationId: "PatchTeam", tags: new[] { "Teams" }, Summary = "Update team", Description = "Updates an existing team's name or primary contact information. Only league admins can update teams.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Division code (e.g., '10U', '12U')")]
    [OpenApiParameter(name: "teamId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Team identifier")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpsertTeamReq), Required = true, Description = "Team update request with optional name and primaryContact fields")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(TeamDto), Description = "Team updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request body")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins can update teams")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Team not found")]
    public async Task<HttpResponseData> PatchTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "teams/{division}/{teamId}")] HttpRequestData req,
        string division,
        string teamId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - league admin OR coach editing their own team
            var isGlobalAdmin = await _membershipRepo.IsGlobalAdminAsync(me.UserId);
            var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
            var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
            var isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
            var isCoach = string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase);

            // Check if coach is assigned to this specific team
            var coachTeamDivision = (membership?.GetString("Division") ?? "").Trim();
            var coachTeamId = (membership?.GetString("TeamId") ?? "").Trim();
            var isOwnTeam = isCoach &&
                            string.Equals(coachTeamDivision, division, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(coachTeamId, teamId, StringComparison.OrdinalIgnoreCase);

            if (!isGlobalAdmin && !isLeagueAdmin && !isOwnTeam)
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only league admins or assigned coaches can update this team");
            }

            var body = await HttpUtil.ReadJsonAsync<UpsertTeamReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");
            ApiGuards.EnsureValidTableKeyPart("division", division);
            ApiGuards.EnsureValidTableKeyPart("teamId", teamId);

            var e = await _teamRepo.GetTeamAsync(leagueId, division, teamId);
            if (e is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "team not found");
            }

            if (!string.IsNullOrWhiteSpace(body.name)) e["Name"] = body.name!.Trim();

            if (body.primaryContact is not null)
            {
                e["PrimaryContactName"] = (body.primaryContact.name ?? "").Trim();
                e["PrimaryContactEmail"] = (body.primaryContact.email ?? "").Trim();
                e["PrimaryContactPhone"] = (body.primaryContact.phone ?? "").Trim();
            }

            if (body.assistantCoaches is not null)
            {
                var assistantCoachesJson = body.assistantCoaches.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(body.assistantCoaches)
                    : "";
                e["AssistantCoaches"] = assistantCoachesJson;
            }

            if (body.clinicPreference is not null)
            {
                e["ClinicPreference"] = body.clinicPreference.Trim();
            }

            if (body.onboardingComplete.HasValue)
            {
                e["OnboardingComplete"] = body.onboardingComplete.Value;
            }

            e["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await _teamRepo.UpdateTeamAsync(e);

            return ApiResponses.Ok(req, ToDto(e));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "PatchTeam failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("DeleteTeam")]
    [OpenApiOperation(operationId: "DeleteTeam", tags: new[] { "Teams" }, Summary = "Delete team", Description = "Deletes a team from a division. Only league admins can delete teams.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Division code (e.g., '10U', '12U')")]
    [OpenApiParameter(name: "teamId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Team identifier")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Team deleted successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins can delete teams")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Team not found")]
    public async Task<HttpResponseData> DeleteTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "teams/{division}/{teamId}")] HttpRequestData req,
        string division,
        string teamId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - league admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can delete teams");
                }
            }

            ApiGuards.EnsureValidTableKeyPart("division", division);
            ApiGuards.EnsureValidTableKeyPart("teamId", teamId);

            await _teamRepo.DeleteTeamAsync(leagueId, division, teamId);
            return ApiResponses.Ok(req, new { ok = true });
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "team not found");
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeleteTeam failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
