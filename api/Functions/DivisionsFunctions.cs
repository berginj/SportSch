using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class DivisionsFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public DivisionsFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
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
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "divisions")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
            var list = new List<DivisionDto>();
            await foreach (var e in table.QueryAsync<TableEntity>(x => x.PartitionKey == DivPk(leagueId)))
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
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
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
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
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

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
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
                await table.AddEntityAsync(e);
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
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
            var body = await HttpUtil.ReadJsonAsync<UpdateReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");
            ApiGuards.EnsureValidTableKeyPart("code", code);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
            TableEntity e;
            try
            {
                e = (await table.GetEntityAsync<TableEntity>(DivPk(leagueId), code)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "division not found");
            }

            if (!string.IsNullOrWhiteSpace(body.name)) e["Name"] = body.name!.Trim();
            if (body.isActive.HasValue) e["IsActive"] = body.isActive.Value;
            e["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);

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
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
            ApiGuards.EnsureValidTableKeyPart("code", code);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
            try
            {
                var e = (await table.GetEntityAsync<TableEntity>(DivPk(leagueId), code)).Value;
                return ApiResponses.Ok(req, new { season = ReadSeasonConfig(e) });
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "division not found");
            }
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
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
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

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
            TableEntity e;
            try
            {
                e = (await table.GetEntityAsync<TableEntity>(DivPk(leagueId), code)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
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

            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);

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
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
            try
            {
                var e = (await table.GetEntityAsync<TableEntity>(TemplatesPk(leagueId), TemplatesRk)).Value;
                var json = (e.GetString("TemplatesJson") ?? "[]").Trim();
                var templates = JsonSerializer.Deserialize<List<DivisionTemplateItem>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
                return ApiResponses.Ok(req, templates);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Ok(req, new List<DivisionTemplateItem>());
            }
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
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
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

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Divisions);
            var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var e = new TableEntity(TemplatesPk(leagueId), TemplatesRk)
            {
                ["LeagueId"] = leagueId,
                ["TemplatesJson"] = json,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            await table.UpsertEntityAsync(e, TableUpdateMode.Replace);
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
