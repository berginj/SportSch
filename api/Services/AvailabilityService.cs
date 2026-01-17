using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;
using System.Net;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of IAvailabilityService for field availability rules and exceptions.
/// </summary>
public class AvailabilityService : IAvailabilityService
{
    private readonly TableServiceClient _tableService;
    private readonly IFieldRepository _fieldRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(
        TableServiceClient tableService,
        IFieldRepository fieldRepo,
        IMembershipRepository membershipRepo,
        ILogger<AvailabilityService> logger)
    {
        _tableService = tableService;
        _fieldRepo = fieldRepo;
        _membershipRepo = membershipRepo;
        _logger = logger;
    }

    public async Task<object> CreateRuleAsync(CreateAvailabilityRuleRequest request, CorrelationContext context)
    {
        _logger.LogInformation("Creating availability rule for league {LeagueId}, field {FieldKey}, correlation {CorrelationId}",
            request.LeagueId, request.FieldKey, context.CorrelationId);

        // Validate dates
        if (!DateOnly.TryParseExact(request.StartsOn, "yyyy-MM-dd", out var startDate))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "startsOn must be YYYY-MM-DD.");
        }
        if (!DateOnly.TryParseExact(request.EndsOn, "yyyy-MM-dd", out var endDate))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "endsOn must be YYYY-MM-DD.");
        }
        if (endDate < startDate)
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE_RANGE, "endsOn must be on or after startsOn.");
        }

        // Validate time range
        if (!TimeUtil.IsValidRange(request.StartTimeLocal, request.EndTimeLocal, out _, out _, out var timeErr))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME_RANGE, timeErr);
        }

        // Validate days of week
        if (request.DaysOfWeek.Count == 0)
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "daysOfWeek is required");
        }

        // Validate division
        ApiGuards.EnsureValidTableKeyPart("division", request.Division);

        // Build partition key from field key
        if (!TryBuildRulePk(request.LeagueId, request.FieldKey, out var pk))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_FIELD_KEY, "fieldKey must be parkCode/fieldCode.");
        }

        // Validate field exists
        if (!FieldKeyUtil.TryParseFieldKey(request.FieldKey, out var parkCode, out var fieldCode))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_FIELD_KEY, "fieldKey must be parkCode/fieldCode.");
        }
        await EnsureFieldExistsAsync(request.LeagueId, parkCode, fieldCode);

        // Generate or validate rule ID
        var ruleId = string.IsNullOrWhiteSpace(request.RuleId) ? Guid.NewGuid().ToString("N") : request.RuleId.Trim();
        ApiGuards.EnsureValidTableKeyPart("ruleId", ruleId);

        // Create entity
        var now = DateTimeOffset.UtcNow;
        var entity = new TableEntity(pk, ruleId)
        {
            [Constants.FieldAvailabilityColumns.FieldKey] = request.FieldKey,
            [Constants.FieldAvailabilityColumns.Division] = request.Division,
            [Constants.FieldAvailabilityColumns.DivisionIds] = string.Join(',', request.DivisionIds.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))),
            [Constants.FieldAvailabilityColumns.StartsOn] = startDate.ToString("yyyy-MM-dd"),
            [Constants.FieldAvailabilityColumns.EndsOn] = endDate.ToString("yyyy-MM-dd"),
            [Constants.FieldAvailabilityColumns.DaysOfWeek] = string.Join(',', request.DaysOfWeek),
            [Constants.FieldAvailabilityColumns.StartTimeLocal] = request.StartTimeLocal,
            [Constants.FieldAvailabilityColumns.EndTimeLocal] = request.EndTimeLocal,
            [Constants.FieldAvailabilityColumns.RecurrencePattern] = request.RecurrencePattern,
            [Constants.FieldAvailabilityColumns.Timezone] = request.Timezone,
            [Constants.FieldAvailabilityColumns.IsActive] = request.IsActive,
            ["CreatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        var ruleTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        try
        {
            await ruleTable.AddEntityAsync(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.ALREADY_EXISTS, "Rule already exists");
        }

        _logger.LogInformation("Availability rule created: {RuleId}", ruleId);

        return MapRule(entity);
    }

    public async Task<object> UpdateRuleAsync(UpdateAvailabilityRuleRequest request, CorrelationContext context)
    {
        _logger.LogInformation("Updating availability rule {RuleId} for league {LeagueId}, correlation {CorrelationId}",
            request.RuleId, request.LeagueId, context.CorrelationId);

        // Validate dates
        if (!DateOnly.TryParseExact(request.StartsOn, "yyyy-MM-dd", out var startDate))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "startsOn must be YYYY-MM-DD.");
        }
        if (!DateOnly.TryParseExact(request.EndsOn, "yyyy-MM-dd", out var endDate))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "endsOn must be YYYY-MM-DD.");
        }
        if (endDate < startDate)
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE_RANGE, "endsOn must be on or after startsOn.");
        }

        // Validate time range
        if (!TimeUtil.IsValidRange(request.StartTimeLocal, request.EndTimeLocal, out _, out _, out var timeErr))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME_RANGE, timeErr);
        }

        // Validate days of week
        if (request.DaysOfWeek.Count == 0)
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "daysOfWeek is required");
        }

        // Validate division
        ApiGuards.EnsureValidTableKeyPart("division", request.Division);
        ApiGuards.EnsureValidTableKeyPart("ruleId", request.RuleId);

        // Build partition key from field key
        if (!TryBuildRulePk(request.LeagueId, request.FieldKey, out var pk))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_FIELD_KEY, "fieldKey must be parkCode/fieldCode.");
        }

        // Validate field exists
        if (!FieldKeyUtil.TryParseFieldKey(request.FieldKey, out var parkCode, out var fieldCode))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_FIELD_KEY, "fieldKey must be parkCode/fieldCode.");
        }
        await EnsureFieldExistsAsync(request.LeagueId, parkCode, fieldCode);

        // Get existing rule
        var ruleTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        var existing = await TryGetRuleAsync(ruleTable, pk, request.RuleId);
        if (existing is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.RULE_NOT_FOUND, "Rule not found");
        }

        // Update entity
        var now = DateTimeOffset.UtcNow;
        var entity = new TableEntity(pk, request.RuleId)
        {
            [Constants.FieldAvailabilityColumns.FieldKey] = request.FieldKey,
            [Constants.FieldAvailabilityColumns.Division] = request.Division,
            [Constants.FieldAvailabilityColumns.DivisionIds] = string.Join(',', request.DivisionIds.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))),
            [Constants.FieldAvailabilityColumns.StartsOn] = startDate.ToString("yyyy-MM-dd"),
            [Constants.FieldAvailabilityColumns.EndsOn] = endDate.ToString("yyyy-MM-dd"),
            [Constants.FieldAvailabilityColumns.DaysOfWeek] = string.Join(',', request.DaysOfWeek),
            [Constants.FieldAvailabilityColumns.StartTimeLocal] = request.StartTimeLocal,
            [Constants.FieldAvailabilityColumns.EndTimeLocal] = request.EndTimeLocal,
            [Constants.FieldAvailabilityColumns.RecurrencePattern] = request.RecurrencePattern,
            [Constants.FieldAvailabilityColumns.Timezone] = request.Timezone,
            [Constants.FieldAvailabilityColumns.IsActive] = request.IsActive,
            ["CreatedUtc"] = existing.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow,
            ["UpdatedUtc"] = now
        };

        await ruleTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        _logger.LogInformation("Availability rule updated: {RuleId}", request.RuleId);

        return MapRule(entity);
    }

    public async Task<object> DeactivateRuleAsync(string leagueId, string ruleId, string userId)
    {
        _logger.LogInformation("Deactivating availability rule {RuleId} for league {LeagueId}, user {UserId}",
            ruleId, leagueId, userId);

        ApiGuards.EnsureValidTableKeyPart("ruleId", ruleId);

        var rulesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        var filter = $"RowKey eq '{ApiGuards.EscapeOData(ruleId)}'";

        await foreach (var entity in rulesTable.QueryAsync<TableEntity>(filter: filter))
        {
            if (!TryRequireRuleLeague(entity.PartitionKey, leagueId)) continue;

            entity[Constants.FieldAvailabilityColumns.IsActive] = false;
            entity["UpdatedUtc"] = DateTimeOffset.UtcNow;
            await rulesTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge);

            _logger.LogInformation("Availability rule deactivated: {RuleId}", ruleId);

            return MapRule(entity);
        }

        throw new ApiGuards.HttpError(404, ErrorCodes.RULE_NOT_FOUND, "Rule not found");
    }

    public async Task<List<object>> GetRulesAsync(string leagueId, string fieldKey)
    {
        _logger.LogInformation("Getting availability rules for league {LeagueId}, field {FieldKey}", leagueId, fieldKey);

        if (!TryBuildRulePk(leagueId, fieldKey, out var pk))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_FIELD_KEY, "fieldKey must be parkCode/fieldCode.");
        }

        var rulesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
        var rules = new List<object>();

        await foreach (var entity in rulesTable.QueryAsync<TableEntity>(filter: filter))
        {
            rules.Add(MapRule(entity));
        }

        return rules;
    }

    public async Task<object> CreateExceptionAsync(CreateAvailabilityExceptionRequest request, CorrelationContext context)
    {
        _logger.LogInformation("Creating availability exception for rule {RuleId}, league {LeagueId}, correlation {CorrelationId}",
            request.RuleId, request.LeagueId, context.CorrelationId);

        ApiGuards.EnsureValidTableKeyPart("ruleId", request.RuleId);

        // Validate dates
        if (!DateOnly.TryParseExact(request.DateFrom, "yyyy-MM-dd", out var from))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "dateFrom must be YYYY-MM-DD.");
        }
        if (!DateOnly.TryParseExact(request.DateTo, "yyyy-MM-dd", out var to))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "dateTo must be YYYY-MM-DD.");
        }
        if (to < from)
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE_RANGE, "dateTo must be on or after dateFrom.");
        }

        // Validate time range
        if (!TimeUtil.IsValidRange(request.StartTimeLocal, request.EndTimeLocal, out _, out _, out var timeErr))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME_RANGE, timeErr);
        }

        // Verify rule exists
        var rulesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        var filter = $"RowKey eq '{ApiGuards.EscapeOData(request.RuleId)}'";
        var rule = await GetRuleForLeagueAsync(rulesTable, filter, request.LeagueId);
        if (rule is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.RULE_NOT_FOUND, "Rule not found");
        }

        // Generate or validate exception ID
        var exceptionId = string.IsNullOrWhiteSpace(request.ExceptionId) ? Guid.NewGuid().ToString("N") : request.ExceptionId.Trim();
        ApiGuards.EnsureValidTableKeyPart("exceptionId", exceptionId);

        // Create entity
        var now = DateTimeOffset.UtcNow;
        var exTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityExceptions);
        var pk = Constants.Pk.FieldAvailabilityRuleExceptions(request.RuleId);
        var entity = new TableEntity(pk, exceptionId)
        {
            [Constants.FieldAvailabilityExceptionColumns.DateFrom] = from.ToString("yyyy-MM-dd"),
            [Constants.FieldAvailabilityExceptionColumns.DateTo] = to.ToString("yyyy-MM-dd"),
            [Constants.FieldAvailabilityExceptionColumns.StartTimeLocal] = request.StartTimeLocal,
            [Constants.FieldAvailabilityExceptionColumns.EndTimeLocal] = request.EndTimeLocal,
            [Constants.FieldAvailabilityExceptionColumns.Reason] = request.Reason,
            ["CreatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        try
        {
            await exTable.AddEntityAsync(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.ALREADY_EXISTS, "Exception already exists");
        }

        _logger.LogInformation("Availability exception created: {ExceptionId}", exceptionId);

        return MapException(entity);
    }

    public async Task<object> UpdateExceptionAsync(UpdateAvailabilityExceptionRequest request, CorrelationContext context)
    {
        _logger.LogInformation("Updating availability exception {ExceptionId} for rule {RuleId}, league {LeagueId}, correlation {CorrelationId}",
            request.ExceptionId, request.RuleId, request.LeagueId, context.CorrelationId);

        ApiGuards.EnsureValidTableKeyPart("ruleId", request.RuleId);
        ApiGuards.EnsureValidTableKeyPart("exceptionId", request.ExceptionId);

        // Validate dates
        if (!DateOnly.TryParseExact(request.DateFrom, "yyyy-MM-dd", out var from))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "dateFrom must be YYYY-MM-DD.");
        }
        if (!DateOnly.TryParseExact(request.DateTo, "yyyy-MM-dd", out var to))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "dateTo must be YYYY-MM-DD.");
        }
        if (to < from)
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE_RANGE, "dateTo must be on or after dateFrom.");
        }

        // Validate time range
        if (!TimeUtil.IsValidRange(request.StartTimeLocal, request.EndTimeLocal, out _, out _, out var timeErr))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME_RANGE, timeErr);
        }

        // Verify rule exists
        var rulesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        var filter = $"RowKey eq '{ApiGuards.EscapeOData(request.RuleId)}'";
        var rule = await GetRuleForLeagueAsync(rulesTable, filter, request.LeagueId);
        if (rule is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.RULE_NOT_FOUND, "Rule not found");
        }

        // Get existing exception
        var exTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityExceptions);
        var pk = Constants.Pk.FieldAvailabilityRuleExceptions(request.RuleId);
        var existing = await TryGetExceptionAsync(exTable, pk, request.ExceptionId);
        if (existing is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.NOT_FOUND, "Exception not found");
        }

        // Update entity
        var now = DateTimeOffset.UtcNow;
        var entity = new TableEntity(pk, request.ExceptionId)
        {
            [Constants.FieldAvailabilityExceptionColumns.DateFrom] = from.ToString("yyyy-MM-dd"),
            [Constants.FieldAvailabilityExceptionColumns.DateTo] = to.ToString("yyyy-MM-dd"),
            [Constants.FieldAvailabilityExceptionColumns.StartTimeLocal] = request.StartTimeLocal,
            [Constants.FieldAvailabilityExceptionColumns.EndTimeLocal] = request.EndTimeLocal,
            [Constants.FieldAvailabilityExceptionColumns.Reason] = request.Reason,
            ["CreatedUtc"] = existing.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow,
            ["UpdatedUtc"] = now
        };

        await exTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        _logger.LogInformation("Availability exception updated: {ExceptionId}", request.ExceptionId);

        return MapException(entity);
    }

    public async Task DeleteExceptionAsync(string leagueId, string ruleId, string exceptionId, string userId)
    {
        _logger.LogInformation("Deleting availability exception {ExceptionId} for rule {RuleId}, league {LeagueId}, user {UserId}",
            exceptionId, ruleId, leagueId, userId);

        ApiGuards.EnsureValidTableKeyPart("ruleId", ruleId);
        ApiGuards.EnsureValidTableKeyPart("exceptionId", exceptionId);

        // Verify rule exists
        var rulesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        var filter = $"RowKey eq '{ApiGuards.EscapeOData(ruleId)}'";
        var rule = await GetRuleForLeagueAsync(rulesTable, filter, leagueId);
        if (rule is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.RULE_NOT_FOUND, "Rule not found");
        }

        // Delete exception
        var exTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityExceptions);
        var pk = Constants.Pk.FieldAvailabilityRuleExceptions(ruleId);
        try
        {
            await exTable.DeleteEntityAsync(pk, exceptionId);
            _logger.LogInformation("Availability exception deleted: {ExceptionId}", exceptionId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.NOT_FOUND, "Exception not found");
        }
    }

    public async Task<List<object>> ListExceptionsAsync(string leagueId, string ruleId)
    {
        _logger.LogInformation("Listing availability exceptions for rule {RuleId}, league {LeagueId}", ruleId, leagueId);

        ApiGuards.EnsureValidTableKeyPart("ruleId", ruleId);

        // Verify rule exists
        var rulesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
        var filter = $"RowKey eq '{ApiGuards.EscapeOData(ruleId)}'";
        var rule = await GetRuleForLeagueAsync(rulesTable, filter, leagueId);
        if (rule is null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.RULE_NOT_FOUND, "Rule not found");
        }

        // List exceptions
        var exceptionsTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityExceptions);
        var pk = Constants.Pk.FieldAvailabilityRuleExceptions(ruleId);
        var exFilter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
        var results = new List<object>();

        await foreach (var entity in exceptionsTable.QueryAsync<TableEntity>(filter: exFilter))
        {
            results.Add(MapException(entity));
        }

        return results;
    }

    public async Task<List<object>> PreviewSlotsAsync(string leagueId, DateOnly dateFrom, DateOnly dateTo)
    {
        _logger.LogInformation("Previewing availability slots for league {LeagueId}, {DateFrom} to {DateTo}",
            leagueId, dateFrom, dateTo);

        var rules = await LoadActiveRulesAsync(leagueId, dateFrom, dateTo);
        var exceptions = await LoadExceptionsByRuleAsync(rules.Select(r => r.ruleId));
        var slots = ExpandAvailability(rules, exceptions, dateFrom, dateTo);

        return slots.Cast<object>().ToList();
    }

    // ========== Helper Methods ==========

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
        var field = await _fieldRepo.GetFieldAsync(leagueId, parkCode, fieldCode);
        if (field == null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.FIELD_NOT_FOUND, "Field not found.");
        }
    }

    private static bool TryBuildRulePk(string leagueId, string fieldKey, out string pk)
    {
        pk = "";
        if (!FieldKeyUtil.TryParseFieldKey(fieldKey, out var parkCode, out var fieldCode)) return false;
        var safeFieldKey = $"{parkCode}|{fieldCode}";
        pk = Constants.Pk.FieldAvailabilityRules(leagueId, safeFieldKey);
        return true;
    }

    // ========== Preview Logic ==========

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

    private record SlotCandidate(
        string gameDate,
        string startTime,
        string endTime,
        string fieldKey,
        string division
    );

    private async Task<List<AvailabilityRule>> LoadActiveRulesAsync(string leagueId, DateOnly dateFrom, DateOnly dateTo)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityRules);
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
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldAvailabilityExceptions);
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
}
