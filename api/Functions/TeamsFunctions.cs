using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class TeamsFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public TeamsFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<TeamsFunctions>();
    }

    public record ContactDto(string? name, string? email, string? phone);
    public record TeamDto(string division, string teamId, string name, ContactDto primaryContact);
    public record UpsertTeamReq(string? division, string? teamId, string? name, ContactDto? primaryContact);

    private static string TeamPk(string leagueId, string division) => $"TEAM|{leagueId}|{division}";

    private static TeamDto ToDto(TableEntity e)
    {
        return new TeamDto(
            division: (e.GetString("Division") ?? "").Trim(),
            teamId: e.RowKey,
            name: (e.GetString("Name") ?? "").Trim(),
            primaryContact: new ContactDto(
                name: (e.GetString("PrimaryContactName") ?? "").Trim(),
                email: (e.GetString("PrimaryContactEmail") ?? "").Trim(),
                phone: (e.GetString("PrimaryContactPhone") ?? "").Trim()
            )
        );
    }

    [Function("GetTeams")]
    public async Task<HttpResponseData> GetTeams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "teams")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Teams);
            var list = new List<TeamDto>();

            if (!string.IsNullOrWhiteSpace(division))
            {
                await foreach (var e in table.QueryAsync<TableEntity>(x => x.PartitionKey == TeamPk(leagueId, division)))
                    list.Add(ToDto(e));
            }
            else
            {
                var prefix = $"TEAM|{leagueId}|";
                var next = prefix + "~";
                var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
                await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
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
    public async Task<HttpResponseData> CreateTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "teams")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<UpsertTeamReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            var teamId = (body.teamId ?? "").Trim();
            var name = (body.name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(name))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division, teamId, and name are required");

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Teams);
            var e = new TableEntity(TeamPk(leagueId, division), teamId)
            {
                ["LeagueId"] = leagueId,
                ["Division"] = division,
                ["TeamId"] = teamId,
                ["Name"] = name,
                ["PrimaryContactName"] = (body.primaryContact?.name ?? "").Trim(),
                ["PrimaryContactEmail"] = (body.primaryContact?.email ?? "").Trim(),
                ["PrimaryContactPhone"] = (body.primaryContact?.phone ?? "").Trim(),
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            try
            {
                await table.AddEntityAsync(e);
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
    public async Task<HttpResponseData> PatchTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "teams/{division}/{teamId}")] HttpRequestData req,
        string division,
        string teamId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<UpsertTeamReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Teams);
            TableEntity e;
            try
            {
                e = (await table.GetEntityAsync<TableEntity>(TeamPk(leagueId, division), teamId)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
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

            e["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);

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
    public async Task<HttpResponseData> DeleteTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "teams/{division}/{teamId}")] HttpRequestData req,
        string division,
        string teamId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Teams);
            await table.DeleteEntityAsync(TeamPk(leagueId, division), teamId, ETag.All);
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
