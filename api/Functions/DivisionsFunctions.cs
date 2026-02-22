using System.Net;
using System.Text.Json;
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

public class DivisionsFunctions
{
    private readonly IDivisionRepository _divisionRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger _log;

    public DivisionsFunctions(
        IDivisionRepository divisionRepo,
        IMembershipRepository membershipRepo,
        ILoggerFactory lf)
    {
        _divisionRepo = divisionRepo;
        _membershipRepo = membershipRepo;
        _log = lf.CreateLogger<DivisionsFunctions>();
    }

    public record DivisionDto(string code, string name, bool isActive);
    public record CreateReq(string? code, string? name, bool? isActive);
    public record UpdateReq(string? name, bool? isActive);

    public record DivisionTemplateItem(string code, string name);
    public record PatchTemplatesReq(List<DivisionTemplateItem>? templates);
    public record BlackoutRange(string? startDate, string? endDate, string? label);
    public record SeasonConfig(
        string? springStart,
        string? springEnd,
        string? fallStart,
        string? fallEnd,
        int gameLengthMinutes,
        List<BlackoutRange> blackouts
    );
    public record PatchSeasonReq(SeasonConfig? season);

    private static string DivPk(string leagueId) => Constants.Pk.Divisions(leagueId);
    private static string TemplatesPk(string leagueId) => $"DIVTEMPLATE|{leagueId}";
    private const string TemplatesRk = "CATALOG";
    private const string SeasonSpringStart = "SeasonSpringStart";
    private const string SeasonSpringEnd = "SeasonSpringEnd";
    private const string SeasonFallStart = "SeasonFallStart";
    private const string SeasonFallEnd = "SeasonFallEnd";
    private const string SeasonGameLengthMinutes = "SeasonGameLengthMinutes";
    private const string SeasonBlackouts = "SeasonBlackouts";

    [Function("GetDivisions")]
    [OpenApiOperation(operationId: "GetDivisions", tags: new[] { "Divisions" }, Summary = "Get divisions", Description = "Retrieves all divisions for a league with their names and active status.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Divisions retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Not a member of this league")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "divisions")] HttpRequestData req)
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
                    "Only league members can view divisions");
            }

            var entities = await _divisionRepo.QueryDivisionsAsync(leagueId);
            var list = new List<DivisionDto>();
            foreach (var e in entities)
            {
                list.Add(new DivisionDto(
                    code: e.RowKey,
                    name: (e.GetString("Name") ?? "").Trim(),
                    isActive: e.GetBoolean("IsActive") ?? true
                ));
            }

            return ApiResponses.Ok(req, list.OrderBy(x => x.code));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetDivisions failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An unexpected error occurred",
                new { requestId, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    [Function("CreateDivision")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "divisions")] HttpRequestData req)
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
                        "Only league admins can create divisions");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<CreateReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var code = (body.code ?? "").Trim();
            var name = (body.name ?? "").Trim();
            var isActive = body.isActive ?? true;

            if (string.IsNullOrWhiteSpace(code))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "code is required");
            if (string.IsNullOrWhiteSpace(name))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "name is required");
            ApiGuards.EnsureValidTableKeyPart("code", code);

            var e = new TableEntity(DivPk(leagueId), code)
            {
                ["LeagueId"] = leagueId,
                ["Code"] = code,
                ["Name"] = name,
                ["IsActive"] = isActive,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            try
            {
                await _divisionRepo.CreateDivisionAsync(e);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", $"division already exists: {code}");
            }
            catch (RequestFailedException ex)
            {
                var requestId = req.FunctionContext.InvocationId.ToString();
                _log.LogError(ex, "CreateDivision storage request failed. requestId={requestId}", requestId);
                return ApiResponses.Error(
                    req,
                    HttpStatusCode.BadGateway,
                    "STORAGE_ERROR",
                    "Storage request failed. This usually means a key contains invalid characters.",
                    new { requestId, status = ex.Status, code = ex.ErrorCode });
            }

            return ApiResponses.Ok(req, new DivisionDto(code, name, isActive), HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateDivision failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("UpdateDivision")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "divisions/{code}")] HttpRequestData req,
        string code)
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
                        "Only league admins can update divisions");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<UpdateReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");
            ApiGuards.EnsureValidTableKeyPart("code", code);

            var e = await _divisionRepo.GetDivisionAsync(leagueId, code);
            if (e is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "division not found");
            }

            if (!string.IsNullOrWhiteSpace(body.name)) e["Name"] = body.name!.Trim();
            if (body.isActive.HasValue) e["IsActive"] = body.isActive.Value;
            e["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await _divisionRepo.UpdateDivisionAsync(e);

            return ApiResponses.Ok(req, new DivisionDto(code, (e.GetString("Name") ?? "").Trim(), e.GetBoolean("IsActive") ?? true));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            var requestId = req.FunctionContext.InvocationId.ToString();
            _log.LogError(ex, "UpdateDivision storage request failed. requestId={requestId}", requestId);
            return ApiResponses.Error(
                req,
                HttpStatusCode.BadGateway,
                "STORAGE_ERROR",
                "Storage request failed. This usually means a key contains invalid characters.",
                new { requestId, status = ex.Status, code = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateDivision failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("GetDivisionSeason")]
    public async Task<HttpResponseData> GetSeason(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "divisions/{code}/season")] HttpRequestData req,
        string code)
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
                        "Only league admins can view season config");
                }
            }

            ApiGuards.EnsureValidTableKeyPart("code", code);

            var e = await _divisionRepo.GetDivisionAsync(leagueId, code);
            if (e is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "division not found");
            }

            return ApiResponses.Ok(req, new { season = ReadSeasonConfig(e) });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetDivisionSeason failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("PatchDivisionSeason")]
    public async Task<HttpResponseData> PatchSeason(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "divisions/{code}/season")] HttpRequestData req,
        string code)
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
                        "Only league admins can update season config");
                }
            }

            ApiGuards.EnsureValidTableKeyPart("code", code);

            var body = await HttpUtil.ReadJsonAsync<PatchSeasonReq>(req);
            if (body?.season is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "season is required");

            var season = body.season;
            ValidateSeasonDates(season.springStart, season.springEnd, "spring");
            ValidateSeasonDates(season.fallStart, season.fallEnd, "fall");
            if (season.gameLengthMinutes < 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "gameLengthMinutes must be >= 0");

            var blackouts = season.blackouts ?? new List<BlackoutRange>();
            foreach (var b in blackouts)
            {
                ValidateSeasonDates(b.startDate, b.endDate, "blackout");
            }

            var e = await _divisionRepo.GetDivisionAsync(leagueId, code);
            if (e is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "division not found");
            }

            e[SeasonSpringStart] = (season.springStart ?? "").Trim();
            e[SeasonSpringEnd] = (season.springEnd ?? "").Trim();
            e[SeasonFallStart] = (season.fallStart ?? "").Trim();
            e[SeasonFallEnd] = (season.fallEnd ?? "").Trim();
            e[SeasonGameLengthMinutes] = season.gameLengthMinutes;
            e[SeasonBlackouts] = JsonSerializer.Serialize(blackouts, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            e["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await _divisionRepo.UpdateDivisionAsync(e);

            return ApiResponses.Ok(req, new { season = ReadSeasonConfig(e) });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "PatchDivisionSeason failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("GetDivisionTemplates")]
    public async Task<HttpResponseData> GetTemplates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "divisions/templates")] HttpRequestData req)
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
                    "Only league members can view division templates");
            }

            var e = await _divisionRepo.GetTemplatesAsync(leagueId);
            if (e is null)
            {
                return ApiResponses.Ok(req, new List<DivisionTemplateItem>());
            }

            var json = (e.GetString("TemplatesJson") ?? "[]").Trim();
            var templates = JsonSerializer.Deserialize<List<DivisionTemplateItem>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
            return ApiResponses.Ok(req, templates);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetDivisionTemplates failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("PatchDivisionTemplates")]
    public async Task<HttpResponseData> PatchTemplates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "divisions/templates")] HttpRequestData req)
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
                        "Only league admins can update division templates");
                }
            }

            var body = await HttpUtil.ReadJsonAsync<PatchTemplatesReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var templates = body.templates ?? new List<DivisionTemplateItem>();

            // Basic validation
            foreach (var t in templates)
            {
                if (string.IsNullOrWhiteSpace(t.code) || string.IsNullOrWhiteSpace(t.name))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Each template needs code and name");
                ApiGuards.EnsureValidTableKeyPart("template code", t.code);
            }

            var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var e = new TableEntity(TemplatesPk(leagueId), TemplatesRk)
            {
                ["LeagueId"] = leagueId,
                ["TemplatesJson"] = json,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            await _divisionRepo.UpsertTemplatesAsync(e);
            return ApiResponses.Ok(req, templates);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            var requestId = req.FunctionContext.InvocationId.ToString();
            _log.LogError(ex, "PatchDivisionTemplates storage request failed. requestId={requestId}", requestId);
            return ApiResponses.Error(
                req,
                HttpStatusCode.BadGateway,
                "STORAGE_ERROR",
                "Storage request failed. This usually means a key contains invalid characters.",
                new { requestId, status = ex.Status, code = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PatchDivisionTemplates failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static SeasonConfig ReadSeasonConfig(TableEntity e)
    {
        var blackouts = new List<BlackoutRange>();
        var blackoutsRaw = (e.GetString(SeasonBlackouts) ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(blackoutsRaw))
        {
            try
            {
                blackouts = JsonSerializer.Deserialize<List<BlackoutRange>>(blackoutsRaw, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? new List<BlackoutRange>();
            }
            catch
            {
                blackouts = new List<BlackoutRange>();
            }
        }

        return new SeasonConfig(
            springStart: (e.GetString(SeasonSpringStart) ?? "").Trim(),
            springEnd: (e.GetString(SeasonSpringEnd) ?? "").Trim(),
            fallStart: (e.GetString(SeasonFallStart) ?? "").Trim(),
            fallEnd: (e.GetString(SeasonFallEnd) ?? "").Trim(),
            gameLengthMinutes: e.GetInt32(SeasonGameLengthMinutes) ?? 0,
            blackouts: blackouts
        );
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

}
