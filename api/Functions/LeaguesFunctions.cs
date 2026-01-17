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

public class LeaguesFunctions
{
    private readonly ILeagueRepository _leagueRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly TableServiceClient _tableService; // Still needed for cascade delete and apply season to divisions
    private readonly ILogger _log;

    public LeaguesFunctions(
        ILeagueRepository leagueRepo,
        IMembershipRepository membershipRepo,
        TableServiceClient tableService,
        ILoggerFactory lf)
    {
        _leagueRepo = leagueRepo;
        _membershipRepo = membershipRepo;
        _tableService = tableService;
        _log = lf.CreateLogger<LeaguesFunctions>();
    }

    public record LeagueContact(string? name, string? email, string? phone);
    public record BlackoutRange(string? startDate, string? endDate, string? label);
    public record SeasonConfig(
        string? springStart,
        string? springEnd,
        string? fallStart,
        string? fallEnd,
        int gameLengthMinutes,
        List<BlackoutRange> blackouts
    );
    public record LeagueDto(string leagueId, string name, string timezone, string status, LeagueContact contact, SeasonConfig season);
    public record CreateLeagueReq(string? leagueId, string? name, string? timezone);
    public record PatchLeagueReq(string? name, string? timezone, string? status, LeagueContact? contact);
    public record PatchSeasonReq(SeasonConfig? season);

    private const string SeasonSpringStart = "SeasonSpringStart";
    private const string SeasonSpringEnd = "SeasonSpringEnd";
    private const string SeasonFallStart = "SeasonFallStart";
    private const string SeasonFallEnd = "SeasonFallEnd";
    private const string SeasonGameLengthMinutes = "SeasonGameLengthMinutes";
    private const string SeasonBlackouts = "SeasonBlackouts";

    private static LeagueDto ToDto(TableEntity e)
    {
        var blackouts = new List<BlackoutRange>();
        var blackoutsRaw = (e.GetString("Blackouts") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(blackoutsRaw))
        {
            try
            {
                blackouts = System.Text.Json.JsonSerializer.Deserialize<List<BlackoutRange>>(blackoutsRaw) ?? new List<BlackoutRange>();
            }
            catch
            {
                blackouts = new List<BlackoutRange>();
            }
        }

        var gameLengthMinutes = e.GetInt32("GameLengthMinutes") ?? 0;
        return new LeagueDto(
            leagueId: e.RowKey,
            name: (e.GetString("Name") ?? e.RowKey).Trim(),
            timezone: (e.GetString("Timezone") ?? "America/New_York").Trim(),
            status: (e.GetString("Status") ?? "Active").Trim(),
            contact: new LeagueContact(
                name: (e.GetString("ContactName") ?? "").Trim(),
                email: (e.GetString("ContactEmail") ?? "").Trim(),
                phone: (e.GetString("ContactPhone") ?? "").Trim()
            ),
            season: new SeasonConfig(
                springStart: (e.GetString("SpringStart") ?? "").Trim(),
                springEnd: (e.GetString("SpringEnd") ?? "").Trim(),
                fallStart: (e.GetString("FallStart") ?? "").Trim(),
                fallEnd: (e.GetString("FallEnd") ?? "").Trim(),
                gameLengthMinutes: gameLengthMinutes,
                blackouts: blackouts
            )
        );
    }

    [Function("ListLeagues")]
    [OpenApiOperation(operationId: "ListLeagues", tags: new[] { "Leagues" }, Summary = "List active leagues", Description = "Retrieves all active leagues (Status='Active'). Public endpoint available to all users.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Active leagues retrieved successfully")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "leagues")] HttpRequestData req)
    {
        try
        {
            var entities = await _leagueRepo.QueryLeaguesAsync(includeAll: false);
            var list = new List<LeagueDto>();
            foreach (var e in entities)
                list.Add(ToDto(e));

            return ApiResponses.Ok(req, list.OrderBy(x => x.name).ThenBy(x => x.leagueId));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListLeagues failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("GetLeague")]
    [OpenApiOperation(operationId: "GetLeague", tags: new[] { "Leagues" }, Summary = "Get league details", Description = "Retrieves detailed information about a specific league including season configuration. Only league members can access.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(LeagueDto), Description = "League details retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league members can view league details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "League not found")]
    public async Task<HttpResponseData> GetLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "league")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be member
            if (!await _membershipRepo.IsMemberAsync(me.UserId, leagueId) &&
                !await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only league members can view league details");
            }

            var e = await _leagueRepo.GetLeagueAsync(leagueId);
            if (e is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
            }

            return ApiResponses.Ok(req, ToDto(e));
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
    [OpenApiOperation(operationId: "PatchLeague", tags: new[] { "Leagues" }, Summary = "Update league", Description = "Updates league metadata (name, timezone, status, contact). Only league admins can update leagues.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(PatchLeagueReq), Required = true, Description = "League update request with optional name, timezone, status, and contact fields")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(LeagueDto), Description = "League updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request body")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins can update league")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "League not found")]
    public async Task<HttpResponseData> PatchLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "league")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - league admin required
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can update league");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<PatchLeagueReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var e = await _leagueRepo.GetLeagueAsync(leagueId);
            if (e is null)
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
            await _leagueRepo.UpdateLeagueAsync(e);

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

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can list all leagues");
            }

            var entities = await _leagueRepo.QueryLeaguesAsync(includeAll: true);
            var list = new List<LeagueDto>();
            foreach (var e in entities)
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

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can list all leagues");
            }

            var entities = await _leagueRepo.QueryLeaguesAsync(includeAll: true);
            var list = new List<LeagueDto>();
            foreach (var e in entities)
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

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can create leagues");
            }

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
                ["SpringStart"] = "",
                ["SpringEnd"] = "",
                ["FallStart"] = "",
                ["FallEnd"] = "",
                ["GameLengthMinutes"] = 0,
                ["Blackouts"] = "[]",
                ["CreatedUtc"] = now,
                ["UpdatedUtc"] = now
            };

            try
            {
                await _leagueRepo.CreateLeagueAsync(e);
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

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can create leagues");
            }

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
                ["SpringStart"] = "",
                ["SpringEnd"] = "",
                ["FallStart"] = "",
                ["FallEnd"] = "",
                ["GameLengthMinutes"] = 0,
                ["Blackouts"] = "[]",
                ["CreatedUtc"] = now,
                ["UpdatedUtc"] = now
            };

            try
            {
                await _leagueRepo.CreateLeagueAsync(e);
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

    [Function("PatchLeagueSeason_Admin")]
    public async Task<HttpResponseData> PatchSeasonAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "admin/leagues/{leagueId}/season")] HttpRequestData req,
        string leagueId)
    {
        return await PatchSeasonCore(req, leagueId, requireGlobal: false);
    }

    [Function("PatchLeagueSeason")]
    [OpenApiOperation(operationId: "PatchLeagueSeason", tags: new[] { "Leagues" }, Summary = "Update league season", Description = "Updates league season configuration (spring/fall dates, game length, blackouts). Only league admins can update season settings. Changes are propagated to all divisions.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(PatchSeasonReq), Required = true, Description = "Season configuration with spring/fall dates, gameLengthMinutes, and blackout ranges")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(LeagueDto), Description = "Season updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid season configuration or date format")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins can update season")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "League not found")]
    public async Task<HttpResponseData> PatchSeason(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "league/season")] HttpRequestData req)
    {
        var leagueId = ApiGuards.RequireLeagueId(req);
        return await PatchSeasonCore(req, leagueId, requireGlobal: false);
    }

    [Function("PatchLeagueSeason_Global")]
    public async Task<HttpResponseData> PatchSeasonGlobal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "global/leagues/{leagueId}/season")] HttpRequestData req,
        string leagueId)
    {
        return await PatchSeasonCore(req, leagueId, requireGlobal: true);
    }

    [Function("DeleteLeague_Global")]
    public async Task<HttpResponseData> DeleteGlobal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "global/leagues/{leagueId}")] HttpRequestData req,
        string leagueId)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);

            // Authorization - global admin only
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Only global admins can delete leagues");
            }

            leagueId = (leagueId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(leagueId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "leagueId is required");
            ApiGuards.EnsureValidTableKeyPart("leagueId", leagueId);

            await DeleteLeagueDataAsync(leagueId);

            try
            {
                await _leagueRepo.DeleteLeagueAsync(leagueId);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
            }

            return ApiResponses.Ok(req, new { leagueId, deleted = true });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeleteLeague_Global failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<HttpResponseData> PatchSeasonCore(HttpRequestData req, string leagueId, bool requireGlobal)
    {
        try
        {
            leagueId = (leagueId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("leagueId", leagueId);

            var me = IdentityUtil.GetMe(req);

            if (requireGlobal)
            {
                // Authorization - global admin only
                if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only global admins can update league season");
                }
            }
            else
            {
                // Authorization - league admin required
                if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
                {
                    var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                    var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                    if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                    {
                        return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                            "Only league admins can update league season");
                    }
                }
            }

            var body = await HttpUtil.ReadJsonAsync<PatchSeasonReq>(req);
            if (body?.season is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "season is required");

            var season = body.season;
            ValidateSeasonDates(season.springStart, season.springEnd, "spring");
            ValidateSeasonDates(season.fallStart, season.fallEnd, "fall");
            if (season.gameLengthMinutes <= 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "gameLengthMinutes must be > 0");

            var blackouts = season.blackouts ?? new List<BlackoutRange>();
            foreach (var b in blackouts)
            {
                ValidateSeasonDates(b.startDate, b.endDate, "blackout");
            }

            var e = await _leagueRepo.GetLeagueAsync(leagueId);
            if (e is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", $"league not found: {leagueId}");
            }

            e["SpringStart"] = (season.springStart ?? "").Trim();
            e["SpringEnd"] = (season.springEnd ?? "").Trim();
            e["FallStart"] = (season.fallStart ?? "").Trim();
            e["FallEnd"] = (season.fallEnd ?? "").Trim();
            e["GameLengthMinutes"] = season.gameLengthMinutes;
            e["Blackouts"] = System.Text.Json.JsonSerializer.Serialize(blackouts);
            e["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await _leagueRepo.UpdateLeagueAsync(e);

            await ApplySeasonToDivisionsAsync(leagueId, season, blackouts);

            return ApiResponses.Ok(req, ToDto(e));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PatchLeagueSeason failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static void ValidateSeasonDates(string? start, string? end, string label)
    {
        var s = (start ?? "").Trim();
        var e = (end ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s) && string.IsNullOrWhiteSpace(e)) return;
        if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", out var sDate))
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, $"{label} startDate must be YYYY-MM-DD.");
        if (!DateOnly.TryParseExact(e, "yyyy-MM-dd", out var eDate))
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, $"{label} endDate must be YYYY-MM-DD.");
        if (eDate < sDate)
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, $"{label} endDate must be on or after startDate.");
    }

    private async Task ApplySeasonToDivisionsAsync(string leagueId, SeasonConfig season, List<BlackoutRange> blackouts)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Divisions);
        var pk = Constants.Pk.Divisions(leagueId);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";

        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            e[SeasonSpringStart] = (season.springStart ?? "").Trim();
            e[SeasonSpringEnd] = (season.springEnd ?? "").Trim();
            e[SeasonFallStart] = (season.fallStart ?? "").Trim();
            e[SeasonFallEnd] = (season.fallEnd ?? "").Trim();
            e[SeasonGameLengthMinutes] = season.gameLengthMinutes;
            e[SeasonBlackouts] = System.Text.Json.JsonSerializer.Serialize(blackouts);
            e["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Merge);
        }
    }

    private async Task DeleteLeagueDataAsync(string leagueId)
    {
        var ruleIds = new List<string>();
        var rulesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        var rulesFilter = PrefixFilter($"AVAILRULE|{leagueId}|");
        await foreach (var rule in rulesTable.QueryAsync<TableEntity>(filter: rulesFilter))
        {
            ruleIds.Add(rule.RowKey);
            await rulesTable.DeleteEntityAsync(rule.PartitionKey, rule.RowKey);
        }

        if (ruleIds.Count > 0)
        {
            var exceptionsTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityExceptions);
            foreach (var ruleId in ruleIds)
            {
                var exceptionFilter = $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.FieldAvailabilityRuleExceptions(ruleId))}'";
                await foreach (var ex in exceptionsTable.QueryAsync<TableEntity>(filter: exceptionFilter))
                    await exceptionsTable.DeleteEntityAsync(ex.PartitionKey, ex.RowKey);
            }
        }

        await DeleteByFilterAsync(Constants.Tables.AccessRequests,
            $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.AccessRequests(leagueId))}'");
        await DeleteByFilterAsync(Constants.Tables.Divisions,
            $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.Divisions(leagueId))}' or PartitionKey eq '{ApiGuards.EscapeOData($"DIVTEMPLATE|{leagueId}")}'");
        await DeleteByFilterAsync(Constants.Tables.Events,
            $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.Events(leagueId))}'");
        await DeleteByFilterAsync(Constants.Tables.Fields, PrefixFilter($"FIELD|{leagueId}|"));
        await DeleteByFilterAsync(Constants.Tables.LeagueInvites,
            $"PartitionKey eq '{ApiGuards.EscapeOData($"LEAGUEINVITE|{leagueId}")}'");
        await DeleteByFilterAsync(Constants.Tables.Memberships,
            $"RowKey eq '{ApiGuards.EscapeOData(leagueId)}'");
        await DeleteByFilterAsync(Constants.Tables.SlotRequests, PrefixFilter($"SLOTREQ|{leagueId}|"));
        await DeleteByFilterAsync(Constants.Tables.Slots, PrefixFilter($"SLOT|{leagueId}|"));
        await DeleteByFilterAsync(Constants.Tables.Teams, PrefixFilter($"TEAM|{leagueId}|"));
        await DeleteByFilterAsync(Constants.Tables.ScheduleRuns, PrefixFilter($"SCHED|{leagueId}|"));
    }

    private static string PrefixFilter(string prefix)
        => $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(prefix + "~")}'";

    private async Task<int> DeleteByFilterAsync(string tableName, string filter)
    {
        var table = await TableClients.GetTableAsync(_tableService, tableName);
        var deleted = 0;

        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All);
            deleted++;
        }

        return deleted;
    }
}
