using System.Net;
using System.Linq;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class AvailabilityFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public AvailabilityFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<AvailabilityFunctions>();
    }

    public record AvailabilityRuleRequest(
        string? ruleId,
        string? fieldKey,
        string? division,
        List<string>? divisionIds,
        string? startsOn,
        string? endsOn,
        List<string>? daysOfWeek,
        string? startTimeLocal,
        string? endTimeLocal,
        string? recurrencePattern,
        string? timezone,
        bool? isActive
    );

    public record AvailabilityRuleExceptionRequest(
        string? exceptionId,
        string? dateFrom,
        string? dateTo,
        string? startTimeLocal,
        string? endTimeLocal,
        string? reason
    );

    public record SlotCandidate(
        string gameDate,
        string startTime,
        string endTime,
        string fieldKey,
        string division
    );

    [Function("CreateAvailabilityRule")]
    public async Task<HttpResponseData> CreateRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "availability/rules")] HttpRequestData req)
    {
        return await UpsertRuleAsync(req, isCreate: true);
    }

    [Function("GetAvailabilityRules")]
    public async Task<HttpResponseData> GetRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "availability/rules")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var fieldKey = (ApiGuards.GetQueryParam(req, "fieldKey") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fieldKey))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey is required");

            if (!TryBuildRulePk(leagueId, fieldKey, out var pk))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey must be parkCode/fieldCode.");

            var rulesTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
            var rules = new List<object>();

            await foreach (var entity in rulesTable.QueryAsync<TableEntity>(filter: filter))
            {
                rules.Add(MapRule(entity));
            }

            return ApiResponses.Ok(req, rules);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetAvailabilityRules failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("UpdateAvailabilityRule")]
    public async Task<HttpResponseData> UpdateRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "availability/rules/{ruleId}")] HttpRequestData req,
        string ruleId)
    {
        return await UpsertRuleAsync(req, isCreate: false, ruleId);
    }

    [Function("DeactivateAvailabilityRule")]
    public async Task<HttpResponseData> DeactivateRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "availability/rules/{ruleId}/deactivate")] HttpRequestData req,
        string ruleId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
            ApiGuards.EnsureValidTableKeyPart("ruleId", ruleId);

            var rulesTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
            var filter = $"RowKey eq '{ApiGuards.EscapeOData(ruleId)}'";

            await foreach (var entity in rulesTable.QueryAsync<TableEntity>(filter: filter))
            {
                if (!TryRequireRuleLeague(entity.PartitionKey, leagueId)) continue;
                entity[Constants.FieldAvailabilityColumns.IsActive] = false;
                entity["UpdatedUtc"] = DateTimeOffset.UtcNow;
                await rulesTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge);
                return ApiResponses.Ok(req, MapRule(entity));
            }

            return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Rule not found");
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeactivateAvailabilityRule failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("CreateAvailabilityException")]
    public async Task<HttpResponseData> CreateException(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "availability/rules/{ruleId}/exceptions")] HttpRequestData req,
        string ruleId)
    {
        return await UpsertExceptionAsync(req, ruleId, isCreate: true);
    }

    [Function("UpdateAvailabilityException")]
    public async Task<HttpResponseData> UpdateException(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "availability/rules/{ruleId}/exceptions/{exceptionId}")] HttpRequestData req,
        string ruleId,
        string exceptionId)
    {
        return await UpsertExceptionAsync(req, ruleId, isCreate: false, exceptionId);
    }

    [Function("DeleteAvailabilityException")]
    public async Task<HttpResponseData> DeleteException(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "availability/rules/{ruleId}/exceptions/{exceptionId}")] HttpRequestData req,
        string ruleId,
        string exceptionId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
            ApiGuards.EnsureValidTableKeyPart("ruleId", ruleId);
            ApiGuards.EnsureValidTableKeyPart("exceptionId", exceptionId);

            var rulesTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
            var filter = $"RowKey eq '{ApiGuards.EscapeOData(ruleId)}'";
            var rule = await GetRuleForLeagueAsync(rulesTable, filter, leagueId);
            if (rule is null)
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Rule not found");

            var exTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityExceptions);
            var pk = Constants.Pk.FieldAvailabilityRuleExceptions(ruleId);
            await exTable.DeleteEntityAsync(pk, exceptionId);
            return ApiResponses.Ok(req, new { deleted = true });
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Exception not found");
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeleteAvailabilityException failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ListAvailabilityExceptions")]
    public async Task<HttpResponseData> ListExceptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "availability/rules/{ruleId}/exceptions")] HttpRequestData req,
        string ruleId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
            ApiGuards.EnsureValidTableKeyPart("ruleId", ruleId);

            var rulesTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
            var filter = $"RowKey eq '{ApiGuards.EscapeOData(ruleId)}'";
            var rule = await GetRuleForLeagueAsync(rulesTable, filter, leagueId);
            if (rule is null)
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Rule not found");

            var exceptionsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityExceptions);
            var pk = Constants.Pk.FieldAvailabilityRuleExceptions(ruleId);
            var exFilter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
            var results = new List<object>();

            await foreach (var entity in exceptionsTable.QueryAsync<TableEntity>(filter: exFilter))
            {
                results.Add(MapException(entity));
            }

            return ApiResponses.Ok(req, results);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListAvailabilityExceptions failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("PreviewAvailabilitySlots")]
    public async Task<HttpResponseData> PreviewAvailabilitySlots(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "availability/preview")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var dateFromRaw = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateToRaw = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();
            if (!DateOnly.TryParseExact(dateFromRaw, "yyyy-MM-dd", out var dateFrom))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!DateOnly.TryParseExact(dateToRaw, "yyyy-MM-dd", out var dateTo))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");
            if (dateTo < dateFrom)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be on or after dateFrom.");

            var rules = await LoadActiveRulesAsync(leagueId, dateFrom, dateTo);
            var exceptions = await LoadExceptionsByRuleAsync(rules.Select(r => r.ruleId));
            var preview = ExpandAvailability(rules, exceptions, dateFrom, dateTo);

            return ApiResponses.Ok(req, new { slots = preview });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PreviewAvailabilitySlots failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<HttpResponseData> UpsertRuleAsync(HttpRequestData req, bool isCreate, string? routeRuleId = null)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<AvailabilityRuleRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var ruleId = (isCreate ? (body.ruleId ?? "") : routeRuleId ?? body.ruleId ?? "").Trim();
            var fieldKey = (body.fieldKey ?? "").Trim();
            var division = (body.division ?? "").Trim();
            var divisionIds = body.divisionIds ?? new List<string>();
            var startsOn = (body.startsOn ?? "").Trim();
            var endsOn = (body.endsOn ?? "").Trim();
            var daysOfWeek = body.daysOfWeek ?? new List<string>();
            var startTime = (body.startTimeLocal ?? "").Trim();
            var endTime = (body.endTimeLocal ?? "").Trim();
            var recurrence = (body.recurrencePattern ?? "Weekly").Trim();
            var timezone = (body.timezone ?? "America/New_York").Trim();
            var isActive = body.isActive ?? true;

            if (string.IsNullOrWhiteSpace(fieldKey))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey is required");
            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "division is required");
            if (!DateOnly.TryParseExact(startsOn, "yyyy-MM-dd", out var startDate))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "startsOn must be YYYY-MM-DD.");
            if (!DateOnly.TryParseExact(endsOn, "yyyy-MM-dd", out var endDate))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "endsOn must be YYYY-MM-DD.");
            if (endDate < startDate)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "endsOn must be on or after startsOn.");
            if (!TimeUtil.IsValidRange(startTime, endTime, out var startMin, out var endMin, out var timeErr))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", timeErr);

            if (daysOfWeek.Count == 0)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "daysOfWeek is required");

            ApiGuards.EnsureValidTableKeyPart("division", division);

            if (!TryBuildRulePk(leagueId, fieldKey, out var pk))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey must be parkCode/fieldCode.");

            var ruleTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
            var resolvedRuleId = string.IsNullOrWhiteSpace(ruleId) ? Guid.NewGuid().ToString("N") : ruleId;
            ApiGuards.EnsureValidTableKeyPart("ruleId", resolvedRuleId);

            if (!TryParseFieldKey(fieldKey, out var parkCode, out var fieldCode))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey must be parkCode/fieldCode.");
            await EnsureFieldExistsAsync(leagueId, parkCode, fieldCode);

            var entity = new TableEntity(pk, resolvedRuleId)
            {
                [Constants.FieldAvailabilityColumns.FieldKey] = fieldKey,
                [Constants.FieldAvailabilityColumns.Division] = division,
                [Constants.FieldAvailabilityColumns.DivisionIds] = string.Join(',', divisionIds.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))),
                [Constants.FieldAvailabilityColumns.StartsOn] = startDate.ToString("yyyy-MM-dd"),
                [Constants.FieldAvailabilityColumns.EndsOn] = endDate.ToString("yyyy-MM-dd"),
                [Constants.FieldAvailabilityColumns.DaysOfWeek] = string.Join(',', daysOfWeek),
                [Constants.FieldAvailabilityColumns.StartTimeLocal] = startTime,
                [Constants.FieldAvailabilityColumns.EndTimeLocal] = endTime,
                [Constants.FieldAvailabilityColumns.RecurrencePattern] = recurrence,
                [Constants.FieldAvailabilityColumns.Timezone] = timezone,
                [Constants.FieldAvailabilityColumns.IsActive] = isActive,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            if (isCreate)
            {
                entity["CreatedUtc"] = DateTimeOffset.UtcNow;
                await ruleTable.AddEntityAsync(entity);
            }
            else
            {
                var existing = await TryGetRuleAsync(ruleTable, pk, resolvedRuleId);
                if (existing is null)
                    return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Rule not found");
                entity["CreatedUtc"] = existing.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow;
                await ruleTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            }

            return ApiResponses.Ok(req, MapRule(entity), isCreate ? HttpStatusCode.Created : HttpStatusCode.OK);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "Rule already exists");
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpsertAvailabilityRule failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<HttpResponseData> UpsertExceptionAsync(HttpRequestData req, string ruleId, bool isCreate, string? routeExceptionId = null)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);
            ApiGuards.EnsureValidTableKeyPart("ruleId", ruleId);

            var body = await HttpUtil.ReadJsonAsync<AvailabilityRuleExceptionRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var exceptionId = (isCreate ? body.exceptionId : routeExceptionId ?? body.exceptionId) ?? "";
            var dateFrom = (body.dateFrom ?? "").Trim();
            var dateTo = (body.dateTo ?? "").Trim();
            var startTime = (body.startTimeLocal ?? "").Trim();
            var endTime = (body.endTimeLocal ?? "").Trim();
            var reason = (body.reason ?? "").Trim();

            if (!DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out var from))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out var to))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");
            if (to < from)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be on or after dateFrom.");
            if (!TimeUtil.IsValidRange(startTime, endTime, out _, out _, out var timeErr))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", timeErr);

            var rulesTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
            var filter = $"RowKey eq '{ApiGuards.EscapeOData(ruleId)}'";
            var rule = await GetRuleForLeagueAsync(rulesTable, filter, leagueId);
            if (rule is null)
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Rule not found");

            var exTable = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityExceptions);
            var pk = Constants.Pk.FieldAvailabilityRuleExceptions(ruleId);
            var resolvedExceptionId = string.IsNullOrWhiteSpace(exceptionId) ? Guid.NewGuid().ToString("N") : exceptionId;
            ApiGuards.EnsureValidTableKeyPart("exceptionId", resolvedExceptionId);

            var entity = new TableEntity(pk, resolvedExceptionId)
            {
                [Constants.FieldAvailabilityExceptionColumns.DateFrom] = from.ToString("yyyy-MM-dd"),
                [Constants.FieldAvailabilityExceptionColumns.DateTo] = to.ToString("yyyy-MM-dd"),
                [Constants.FieldAvailabilityExceptionColumns.StartTimeLocal] = startTime,
                [Constants.FieldAvailabilityExceptionColumns.EndTimeLocal] = endTime,
                [Constants.FieldAvailabilityExceptionColumns.Reason] = reason,
                ["UpdatedUtc"] = DateTimeOffset.UtcNow
            };

            if (isCreate)
            {
                entity["CreatedUtc"] = DateTimeOffset.UtcNow;
                await exTable.AddEntityAsync(entity);
            }
            else
            {
                var existing = await TryGetExceptionAsync(exTable, pk, resolvedExceptionId);
                if (existing is null)
                    return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Exception not found");
                entity["CreatedUtc"] = existing.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow;
                await exTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            }

            return ApiResponses.Ok(req, MapException(entity), isCreate ? HttpStatusCode.Created : HttpStatusCode.OK);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "Exception already exists");
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpsertAvailabilityException failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static object MapRule(TableEntity e)
    {
        return new
        {
            ruleId = e.RowKey,
            fieldKey = e.GetString(Constants.FieldAvailabilityColumns.FieldKey) ?? "",
            division = e.GetString(Constants.FieldAvailabilityColumns.Division) ?? "",
            divisionIds = SplitList(e.GetString(Constants.FieldAvailabilityColumns.DivisionIds)),
            startsOn = e.GetString(Constants.FieldAvailabilityColumns.StartsOn) ?? "",
            endsOn = e.GetString(Constants.FieldAvailabilityColumns.EndsOn) ?? "",
            daysOfWeek = SplitList(e.GetString(Constants.FieldAvailabilityColumns.DaysOfWeek)),
            startTimeLocal = e.GetString(Constants.FieldAvailabilityColumns.StartTimeLocal) ?? "",
            endTimeLocal = e.GetString(Constants.FieldAvailabilityColumns.EndTimeLocal) ?? "",
            recurrencePattern = e.GetString(Constants.FieldAvailabilityColumns.RecurrencePattern) ?? "",
            timezone = e.GetString(Constants.FieldAvailabilityColumns.Timezone) ?? "",
            isActive = e.GetBoolean(Constants.FieldAvailabilityColumns.IsActive) ?? false
        };
    }

    private static object MapException(TableEntity e)
    {
        return new
        {
            exceptionId = e.RowKey,
            dateFrom = e.GetString(Constants.FieldAvailabilityExceptionColumns.DateFrom) ?? "",
            dateTo = e.GetString(Constants.FieldAvailabilityExceptionColumns.DateTo) ?? "",
            startTimeLocal = e.GetString(Constants.FieldAvailabilityExceptionColumns.StartTimeLocal) ?? "",
            endTimeLocal = e.GetString(Constants.FieldAvailabilityExceptionColumns.EndTimeLocal) ?? "",
            reason = e.GetString(Constants.FieldAvailabilityExceptionColumns.Reason) ?? ""
        };
    }

    private static List<string> SplitList(string? value)
    {
        return (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    private static async Task<TableEntity?> TryGetRuleAsync(TableClient table, string pk, string ruleId)
    {
        try
        {
            var resp = await table.GetEntityAsync<TableEntity>(pk, ruleId);
            return resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static async Task<TableEntity?> TryGetExceptionAsync(TableClient table, string pk, string exceptionId)
    {
        try
        {
            var resp = await table.GetEntityAsync<TableEntity>(pk, exceptionId);
            return resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static async Task<TableEntity?> GetRuleForLeagueAsync(TableClient table, string filter, string leagueId)
    {
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            if (!TryRequireRuleLeague(entity.PartitionKey, leagueId)) continue;
            return entity;
        }

        return null;
    }

    private static bool TryRequireRuleLeague(string partitionKey, string leagueId)
    {
        var prefix = $"AVAILRULE|{leagueId}|";
        if (!partitionKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private async Task EnsureFieldExistsAsync(string leagueId, string parkCode, string fieldCode)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
        try
        {
            _ = await table.GetEntityAsync<TableEntity>(Constants.Pk.Fields(leagueId, parkCode), fieldCode);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, "Field not found.");
        }
    }

    private record AvailabilityRule(
        string ruleId,
        string fieldKey,
        string division,
        HashSet<DayOfWeek> days,
        DateOnly startsOn,
        DateOnly endsOn,
        string startTime,
        string endTime,
        bool isActive
    );

    private record AvailabilityException(
        string exceptionId,
        string dateFrom,
        string dateTo,
        string startTime,
        string endTime
    );

    private async Task<List<AvailabilityRule>> LoadActiveRulesAsync(string leagueId, DateOnly dateFrom, DateOnly dateTo)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
        var prefix = $"AVAILRULE|{leagueId}|";
        var next = prefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
        var rules = new List<AvailabilityRule>();

        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            var isActive = entity.GetBoolean(Constants.FieldAvailabilityColumns.IsActive) ?? false;
            if (!isActive) continue;

            var startsOnRaw = entity.GetString(Constants.FieldAvailabilityColumns.StartsOn) ?? "";
            var endsOnRaw = entity.GetString(Constants.FieldAvailabilityColumns.EndsOn) ?? "";
            if (!DateOnly.TryParseExact(startsOnRaw, "yyyy-MM-dd", out var startsOn)) continue;
            if (!DateOnly.TryParseExact(endsOnRaw, "yyyy-MM-dd", out var endsOn)) continue;
            if (endsOn < dateFrom || startsOn > dateTo) continue;

            var startTime = (entity.GetString(Constants.FieldAvailabilityColumns.StartTimeLocal) ?? "").Trim();
            var endTime = (entity.GetString(Constants.FieldAvailabilityColumns.EndTimeLocal) ?? "").Trim();
            if (!TimeUtil.IsValidRange(startTime, endTime, out _, out _, out _)) continue;

            var days = NormalizeDays(SplitList(entity.GetString(Constants.FieldAvailabilityColumns.DaysOfWeek)));
            if (days.Count == 0) continue;

            rules.Add(new AvailabilityRule(
                entity.RowKey,
                entity.GetString(Constants.FieldAvailabilityColumns.FieldKey) ?? "",
                entity.GetString(Constants.FieldAvailabilityColumns.Division) ?? "",
                days,
                startsOn,
                endsOn,
                startTime,
                endTime,
                isActive
            ));
        }

        return rules;
    }

    private async Task<Dictionary<string, List<AvailabilityException>>> LoadExceptionsByRuleAsync(IEnumerable<string> ruleIds)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityExceptions);
        var results = new Dictionary<string, List<AvailabilityException>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ruleId in ruleIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pk = Constants.Pk.FieldAvailabilityRuleExceptions(ruleId);
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
            var list = new List<AvailabilityException>();

            await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
            {
                list.Add(new AvailabilityException(
                    entity.RowKey,
                    entity.GetString(Constants.FieldAvailabilityExceptionColumns.DateFrom) ?? "",
                    entity.GetString(Constants.FieldAvailabilityExceptionColumns.DateTo) ?? "",
                    entity.GetString(Constants.FieldAvailabilityExceptionColumns.StartTimeLocal) ?? "",
                    entity.GetString(Constants.FieldAvailabilityExceptionColumns.EndTimeLocal) ?? ""
                ));
            }

            results[ruleId] = list;
        }

        return results;
    }

    private static List<SlotCandidate> ExpandAvailability(
        List<AvailabilityRule> rules,
        Dictionary<string, List<AvailabilityException>> exceptions,
        DateOnly dateFrom,
        DateOnly dateTo)
    {
        var slots = new List<SlotCandidate>();

        foreach (var rule in rules)
        {
            if (!rule.isActive) continue;
            var ruleStart = rule.startsOn < dateFrom ? dateFrom : rule.startsOn;
            var ruleEnd = rule.endsOn > dateTo ? dateTo : rule.endsOn;

            for (var date = ruleStart; date <= ruleEnd; date = date.AddDays(1))
            {
                if (!rule.days.Contains(date.DayOfWeek)) continue;
                if (IsException(exceptions, rule.ruleId, date, rule.startTime, rule.endTime)) continue;

                slots.Add(new SlotCandidate(
                    date.ToString("yyyy-MM-dd"),
                    rule.startTime,
                    rule.endTime,
                    rule.fieldKey,
                    rule.division
                ));
            }
        }

        return slots;
    }

    private static bool IsException(
        Dictionary<string, List<AvailabilityException>> exceptions,
        string ruleId,
        DateOnly date,
        string startTime,
        string endTime)
    {
        if (!exceptions.TryGetValue(ruleId, out var list)) return false;
        foreach (var ex in list)
        {
            if (!DateOnly.TryParseExact(ex.dateFrom, "yyyy-MM-dd", out var from)) continue;
            if (!DateOnly.TryParseExact(ex.dateTo, "yyyy-MM-dd", out var to)) continue;
            if (date < from || date > to) continue;
            if (!TimeUtil.IsValidRange(ex.startTime, ex.endTime, out var exStart, out var exEnd, out _)) continue;
            if (!TimeUtil.IsValidRange(startTime, endTime, out var ruleStart, out var ruleEnd, out _)) continue;
            if (TimeUtil.Overlaps(ruleStart, ruleEnd, exStart, exEnd)) return true;
        }

        return false;
    }

    private static HashSet<DayOfWeek> NormalizeDays(List<string>? days)
    {
        var set = new HashSet<DayOfWeek>();
        if (days is null) return set;
        foreach (var raw in days)
        {
            var key = (raw ?? "").Trim().ToLowerInvariant();
            if (key.StartsWith("sun")) set.Add(DayOfWeek.Sunday);
            else if (key.StartsWith("mon")) set.Add(DayOfWeek.Monday);
            else if (key.StartsWith("tue")) set.Add(DayOfWeek.Tuesday);
            else if (key.StartsWith("wed")) set.Add(DayOfWeek.Wednesday);
            else if (key.StartsWith("thu")) set.Add(DayOfWeek.Thursday);
            else if (key.StartsWith("fri")) set.Add(DayOfWeek.Friday);
            else if (key.StartsWith("sat")) set.Add(DayOfWeek.Saturday);
        }
        return set;
    }

    private static bool TryParseFieldKey(string raw, out string parkCode, out string fieldCode)
    {
        parkCode = "";
        fieldCode = "";
        var v = (raw ?? "").Trim().Trim('/');
        var parts = v.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        parkCode = Slug.Make(parts[0]);
        fieldCode = Slug.Make(parts[1]);
        return !string.IsNullOrWhiteSpace(parkCode) && !string.IsNullOrWhiteSpace(fieldCode);
    }

    private static bool TryBuildRulePk(string leagueId, string fieldKey, out string pk)
    {
        pk = "";
        if (!TryParseFieldKey(fieldKey, out var parkCode, out var fieldCode)) return false;
        var safeFieldKey = $"{parkCode}|{fieldCode}";
        pk = Constants.Pk.FieldAvailabilityRules(leagueId, safeFieldKey);
        return true;
    }
}
