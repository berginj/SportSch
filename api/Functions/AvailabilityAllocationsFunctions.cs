using System.Net;
using System.Text;
using System.Globalization;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

public class AvailabilityAllocationsFunctions
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public AvailabilityAllocationsFunctions(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<AvailabilityAllocationsFunctions>();
        _svc = tableServiceClient;
    }

    public record AllocationDto(
        string allocationId,
        string scope,
        string fieldKey,
        string division,
        string startsOn,
        string endsOn,
        List<string> daysOfWeek,
        string startTimeLocal,
        string endTimeLocal,
        string slotType,
        int? priorityRank,
        string notes,
        bool isActive
    );

    public record ClearAllocationsRequest(
        string? scope,
        string? dateFrom,
        string? dateTo,
        string? fieldKey
    );

    private const string ScopeLeague = "LEAGUE";

    private record AllocationSpec(
        string fieldKey,
        DateOnly dateFrom,
        DateOnly dateTo,
        int startMin,
        int endMin,
        HashSet<DayOfWeek> days);

    [Function("ImportAvailabilityAllocations")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/availability-allocations")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var csvText = await CsvUpload.ReadCsvTextAsync(req);
            if (string.IsNullOrWhiteSpace(csvText))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Empty CSV body.");

            var rows = CsvMini.Parse(csvText);
            if (rows.Count < 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No CSV rows found.");

            var header = rows[0];
            var idx = CsvMini.HeaderIndex(header);

            var required = new[] { "fieldkey", "datefrom", "dateto", "starttime", "endtime" };
            var missing = required.Where(c => !idx.ContainsKey(c)).ToList();
            if (missing.Count > 0)
            {
                var headerPreview = string.Join(",", header.Select(x => (x ?? "").Trim()).Take(12));
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST",
                    "Missing required columns. Required: fieldKey, dateFrom, dateTo, startTime, endTime. Optional: division, daysOfWeek, slotType, priorityRank, notes, isActive.",
                    new { headerPreview, missing });
            }

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityAllocations);
            var fieldsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
            var fieldKeys = await LoadFieldKeysAsync(fieldsTable, leagueId);
            var existing = await LoadExistingAllocationsAsync(table, leagueId);
            var slotsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
            var slotRanges = await LoadExistingSlotRangesAsync(rows, idx, slotsTable, leagueId);

            int upserted = 0, rejected = 0, skipped = 0;
            var errors = new List<object>();
            var warnings = new List<object>();
            var actionsByPartition = new Dictionary<string, List<TableTransactionAction>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (CsvMini.IsBlankRow(r)) { skipped++; continue; }

                var fieldKeyRaw = CsvMini.Get(r, idx, "fieldkey").Trim();
                var divisionRaw = CsvMini.Get(r, idx, "division").Trim();
                var dateFrom = CsvMini.Get(r, idx, "datefrom").Trim();
                var dateTo = CsvMini.Get(r, idx, "dateto").Trim();
                var startTime = CsvMini.Get(r, idx, "starttime").Trim();
                var endTime = CsvMini.Get(r, idx, "endtime").Trim();
                var daysRaw = CsvMini.Get(r, idx, "daysofweek").Trim();
                var slotTypeRaw = CsvMini.Get(r, idx, "slottype").Trim();
                var priorityRankRaw = CsvMini.Get(r, idx, "priorityrank").Trim();
                var notes = CsvMini.Get(r, idx, "notes").Trim();
                var isActiveRaw = CsvMini.Get(r, idx, "isactive").Trim();

                if (string.IsNullOrWhiteSpace(fieldKeyRaw) || string.IsNullOrWhiteSpace(dateFrom) ||
                    string.IsNullOrWhiteSpace(dateTo) || string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "fieldKey, dateFrom, dateTo, startTime, endTime are required." });
                    continue;
                }

                if (!TryParseFieldKey(fieldKeyRaw, out var parkCode, out var fieldCode))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "Invalid fieldKey. Use parkCode/fieldCode." });
                    continue;
                }
                if (!TryNormalizeDate(dateFrom, out var dateFromNorm) ||
                    !DateOnly.TryParseExact(dateFromNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateFromVal))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "dateFrom must be YYYY-MM-DD." });
                    continue;
                }
                if (!TryNormalizeDate(dateTo, out var dateToNorm) ||
                    !DateOnly.TryParseExact(dateToNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateToVal))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "dateTo must be YYYY-MM-DD." });
                    continue;
                }
                if (dateToVal < dateFromVal)
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "dateTo must be on or after dateFrom." });
                    continue;
                }

                var scope = string.IsNullOrWhiteSpace(divisionRaw) ? ScopeLeague : divisionRaw;
                if (!string.Equals(scope, ScopeLeague, StringComparison.OrdinalIgnoreCase))
                {
                    ApiGuards.EnsureValidTableKeyPart("division", scope);
                }

                var normalizedFieldKey = $"{parkCode}/{fieldCode}";
                if (!fieldKeys.Contains(normalizedFieldKey))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "Field not found in GameSwapFields (import fields first).", fieldKey = fieldKeyRaw });
                    continue;
                }

                if (!TryParseDays(daysRaw, out var daysSet))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "daysOfWeek must use Mon/Tue/Wed/Thu/Fri/Sat/Sun.", fieldKey = fieldKeyRaw });
                    continue;
                }

                var startMin = SlotOverlap.ParseMinutes(startTime);
                var endMin = SlotOverlap.ParseMinutes(endTime);
                if (startMin < 0 || endMin <= startMin)
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "startTime/endTime must be valid HH:MM and endTime after startTime." });
                    continue;
                }

                var slotType = NormalizeSlotType(slotTypeRaw);
                if (slotType is null)
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "slotType must be Practice, Game, or Both." });
                    continue;
                }

                if (!TryParsePriorityRank(priorityRankRaw, out var priorityRank))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "priorityRank must be a positive whole number." });
                    continue;
                }
                if (string.Equals(slotType, "practice", StringComparison.OrdinalIgnoreCase))
                    priorityRank = null;

                var allocKey = BuildAllocationKey(normalizedFieldKey, dateFromVal, dateToVal, startTime, endTime, daysSet);
                if (existing.exactKeys.Contains(allocKey))
                {
                    skipped++;
                    warnings.Add(new { row = i + 1, warning = "Allocation already exists for this field/time window.", fieldKey = fieldKeyRaw });
                    continue;
                }

                var spec = new AllocationSpec(normalizedFieldKey, dateFromVal, dateToVal, startMin, endMin, daysSet);
                if (HasAllocationOverlap(existing.byField, spec))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "Allocation overlaps an existing allocation for this field.", fieldKey = fieldKeyRaw });
                    continue;
                }

                existing.exactKeys.Add(allocKey);
                AddAllocation(existing.byField, spec);

                if (AllocationOverlapsSlots(slotRanges, normalizedFieldKey, dateFromVal, dateToVal, startMin, endMin, daysSet))
                {
                    warnings.Add(new { row = i + 1, warning = "Allocation overlaps an existing non-availability slot.", fieldKey = fieldKeyRaw });
                }

                var pk = Constants.Pk.FieldAvailabilityAllocations(leagueId, scope, normalizedFieldKey);
                var allocationId = Guid.NewGuid().ToString("N");
                var daysList = FormatDays(daysSet);
                var isActive = !string.IsNullOrWhiteSpace(isActiveRaw) ? bool.TryParse(isActiveRaw, out var b) && b : true;

                var entity = new TableEntity(pk, allocationId)
                {
                    ["LeagueId"] = leagueId,
                    ["Scope"] = scope,
                    ["FieldKey"] = normalizedFieldKey,
                    ["Division"] = scope,
                    ["StartsOn"] = dateFromNorm,
                    ["EndsOn"] = dateToNorm,
                    ["DaysOfWeek"] = string.Join(",", daysList),
                    ["StartTimeLocal"] = startTime,
                    ["EndTimeLocal"] = endTime,
                    ["SlotType"] = slotType,
                    ["Notes"] = notes,
                    ["IsActive"] = isActive,
                    ["UpdatedUtc"] = DateTimeOffset.UtcNow
                };
                if (priorityRank.HasValue)
                    entity["PriorityRank"] = priorityRank.Value;

                if (!actionsByPartition.TryGetValue(pk, out var actions))
                {
                    actions = new List<TableTransactionAction>(capacity: 100);
                    actionsByPartition[pk] = actions;
                }

                actions.Add(new TableTransactionAction(TableTransactionActionType.UpsertMerge, entity));
                if (actions.Count == 100)
                {
                    var result = await table.SubmitTransactionAsync(actions);
                    upserted += result.Value.Count;
                    actions.Clear();
                }
            }

            foreach (var actions in actionsByPartition.Values)
            {
                if (actions.Count == 0) continue;
                var result = await table.SubmitTransactionAsync(actions);
                upserted += result.Value.Count;
            }

            UsageTelemetry.Track(_log, "api_import_availability_allocations", leagueId, me.UserId, new
            {
                upserted,
                rejected,
                skipped
            });

            return ApiResponses.Ok(req, new { leagueId, upserted, rejected, skipped, errors, warnings });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            var requestId = req.FunctionContext.InvocationId.ToString();
            _log.LogError(ex, "ImportAvailabilityAllocations storage request failed. requestId={requestId}", requestId);
            return ApiResponses.Error(
                req,
                HttpStatusCode.BadGateway,
                "STORAGE_ERROR",
                "Storage request failed. This usually means a key contains invalid characters.",
                new { requestId, status = ex.Status, code = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ImportAvailabilityAllocations failed");
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                "INTERNAL",
                "Internal Server Error",
                new { exception = ex.GetType().Name, message = ex.Message });
        }
    }

    [Function("ListAvailabilityAllocations")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "availability/allocations")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var scope = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();
            var fieldKey = (ApiGuards.GetQueryParam(req, "fieldKey") ?? "").Trim();
            var dateFrom = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateTo = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();
            string dateFromNorm = "";
            string dateToNorm = "";

            if (!string.IsNullOrWhiteSpace(dateFrom) &&
                (!TryNormalizeDate(dateFrom, out dateFromNorm) ||
                 !DateOnly.TryParseExact(dateFromNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!string.IsNullOrWhiteSpace(dateTo) &&
                (!TryNormalizeDate(dateTo, out dateToNorm) ||
                 !DateOnly.TryParseExact(dateToNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityAllocations);
            var list = new List<AllocationDto>();

            string filter;
            if (!string.IsNullOrWhiteSpace(scope))
            {
                ApiGuards.EnsureValidTableKeyPart("division", scope);
                var pkPrefix = $"ALLOC|{leagueId}|{scope}|";
                var next = pkPrefix + "\uffff";
                filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
            }
            else
            {
                var pkPrefix = $"ALLOC|{leagueId}|";
                var next = pkPrefix + "\uffff";
                filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
            }

            if (!string.IsNullOrWhiteSpace(fieldKey))
                filter += $" and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";

            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var startsOn = e.GetString("StartsOn") ?? "";
                var endsOn = e.GetString("EndsOn") ?? "";
                if (!string.IsNullOrWhiteSpace(dateFromNorm) && string.CompareOrdinal(endsOn, dateFromNorm) < 0) continue;
                if (!string.IsNullOrWhiteSpace(dateToNorm) && string.CompareOrdinal(startsOn, dateToNorm) > 0) continue;

                var days = (e.GetString("DaysOfWeek") ?? "")
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                var slotType = NormalizeSlotType(e.GetString("SlotType")) ?? "practice";
                int? priorityRank = e.GetInt32("PriorityRank");
                if (!priorityRank.HasValue)
                {
                    var rawPriority = (e.GetString("PriorityRank") ?? "").Trim();
                    if (int.TryParse(rawPriority, out var parsed) && parsed > 0)
                        priorityRank = parsed;
                }
                if (priorityRank.HasValue && priorityRank.Value <= 0) priorityRank = null;
                if (string.Equals(slotType, "practice", StringComparison.OrdinalIgnoreCase)) priorityRank = null;
                list.Add(new AllocationDto(
                    allocationId: e.RowKey,
                    scope: e.GetString("Scope") ?? ScopeLeague,
                    fieldKey: e.GetString("FieldKey") ?? "",
                    division: e.GetString("Division") ?? "",
                    startsOn: e.GetString("StartsOn") ?? "",
                    endsOn: e.GetString("EndsOn") ?? "",
                    daysOfWeek: days,
                    startTimeLocal: e.GetString("StartTimeLocal") ?? "",
                    endTimeLocal: e.GetString("EndTimeLocal") ?? "",
                    slotType: slotType,
                    priorityRank: priorityRank,
                    notes: e.GetString("Notes") ?? "",
                    isActive: e.GetBoolean("IsActive") ?? true
                ));
            }

            return ApiResponses.Ok(req, list.OrderBy(x => x.scope).ThenBy(x => x.fieldKey));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListAvailabilityAllocations failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("ClearAvailabilityAllocations")]
    public async Task<HttpResponseData> Clear(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "availability/allocations/clear")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<ClearAllocationsRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var requestedScope = (body.scope ?? "").Trim();
            var clearAllScopes = string.IsNullOrWhiteSpace(requestedScope);
            var scope = clearAllScopes
                ? ""
                : string.Equals(requestedScope, ScopeLeague, StringComparison.OrdinalIgnoreCase) ? ScopeLeague : requestedScope;
            if (!clearAllScopes && !string.Equals(scope, ScopeLeague, StringComparison.OrdinalIgnoreCase))
                ApiGuards.EnsureValidTableKeyPart("division", scope);

            var dateFrom = (body.dateFrom ?? "").Trim();
            var dateTo = (body.dateTo ?? "").Trim();
            var fieldKey = (body.fieldKey ?? "").Trim();
            string dateFromNorm = "";
            string dateToNorm = "";

            if (!string.IsNullOrWhiteSpace(dateFrom) &&
                (!TryNormalizeDate(dateFrom, out dateFromNorm) ||
                 !DateOnly.TryParseExact(dateFromNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!string.IsNullOrWhiteSpace(dateTo) &&
                (!TryNormalizeDate(dateTo, out dateToNorm) ||
                 !DateOnly.TryParseExact(dateToNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityAllocations);
            var pkPrefix = clearAllScopes
                ? $"ALLOC|{leagueId}|"
                : $"ALLOC|{leagueId}|{scope}|";
            var next = pkPrefix + "\uffff";
            var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
            if (!string.IsNullOrWhiteSpace(fieldKey))
                filter += $" and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";

            var deleted = 0;
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var startsOn = e.GetString("StartsOn") ?? "";
                var endsOn = e.GetString("EndsOn") ?? "";
                if (!string.IsNullOrWhiteSpace(dateFromNorm) && string.CompareOrdinal(endsOn, dateFromNorm) < 0) continue;
                if (!string.IsNullOrWhiteSpace(dateToNorm) && string.CompareOrdinal(startsOn, dateToNorm) > 0) continue;
                await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, e.ETag);
                deleted++;
            }

            return ApiResponses.Ok(req, new
            {
                leagueId,
                scope = clearAllScopes ? "ALL" : scope,
                dateFrom = dateFromNorm,
                dateTo = dateToNorm,
                fieldKey,
                deleted
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ClearAvailabilityAllocations failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static bool TryParseDays(string raw, out HashSet<DayOfWeek> days)
    {
        days = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(raw)) return true;

        var tokens = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return true;

        foreach (var token in tokens)
        {
            var key = token.Trim().ToLowerInvariant();
            if (key.StartsWith("sun")) days.Add(DayOfWeek.Sunday);
            else if (key.StartsWith("mon")) days.Add(DayOfWeek.Monday);
            else if (key.StartsWith("tue")) days.Add(DayOfWeek.Tuesday);
            else if (key.StartsWith("wed")) days.Add(DayOfWeek.Wednesday);
            else if (key.StartsWith("thu")) days.Add(DayOfWeek.Thursday);
            else if (key.StartsWith("fri")) days.Add(DayOfWeek.Friday);
            else if (key.StartsWith("sat")) days.Add(DayOfWeek.Saturday);
            else return false;
        }

        return true;
    }

    private static string? NormalizeSlotType(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key)) return "practice";
        if (key == "practice") return "practice";
        if (key == "game") return "game";
        if (key == "both") return "both";
        return null;
    }

    private static bool TryParsePriorityRank(string? raw, out int? priorityRank)
    {
        priorityRank = null;
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!int.TryParse(value, out var parsed)) return false;
        if (parsed <= 0) return false;
        priorityRank = parsed;
        return true;
    }

    private static List<string> FormatDays(HashSet<DayOfWeek> days)
    {
        if (days.Count == 0) return new List<string>();
        var ordered = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };
        return ordered.Where(days.Contains).Select(ToDayToken).ToList();
    }

    private static string ToDayToken(DayOfWeek day)
        => day switch
        {
            DayOfWeek.Sunday => "Sun",
            DayOfWeek.Monday => "Mon",
            DayOfWeek.Tuesday => "Tue",
            DayOfWeek.Wednesday => "Wed",
            DayOfWeek.Thursday => "Thu",
            DayOfWeek.Friday => "Fri",
            DayOfWeek.Saturday => "Sat",
            _ => ""
        };

    private static string BuildAllocationKey(
        string fieldKey,
        DateOnly dateFrom,
        DateOnly dateTo,
        string startTime,
        string endTime,
        HashSet<DayOfWeek> days)
    {
        var dayToken = string.Join(",", FormatDays(days));
        return $"{fieldKey}|{dateFrom:yyyy-MM-dd}|{dateTo:yyyy-MM-dd}|{startTime}|{endTime}|{dayToken}";
    }

    private static bool RangesOverlap(DateOnly aStart, DateOnly aEnd, DateOnly bStart, DateOnly bEnd)
        => !(aEnd < bStart || bEnd < aStart);

    private static bool TimesOverlap(int aStart, int aEnd, int bStart, int bEnd)
        => !(aEnd <= bStart || bEnd <= aStart);

    private static bool DaysOverlap(HashSet<DayOfWeek> a, HashSet<DayOfWeek> b)
    {
        if (a.Count == 0 || b.Count == 0) return true;
        return a.Overlaps(b);
    }

    private static bool HasAllocationOverlap(
        Dictionary<string, List<AllocationSpec>> byField,
        AllocationSpec candidate)
    {
        if (!byField.TryGetValue(candidate.fieldKey, out var list)) return false;
        foreach (var existing in list)
        {
            if (!RangesOverlap(existing.dateFrom, existing.dateTo, candidate.dateFrom, candidate.dateTo)) continue;
            if (!TimesOverlap(existing.startMin, existing.endMin, candidate.startMin, candidate.endMin)) continue;
            if (!DaysOverlap(existing.days, candidate.days)) continue;
            return true;
        }
        return false;
    }

    private static void AddAllocation(Dictionary<string, List<AllocationSpec>> byField, AllocationSpec spec)
    {
        if (!byField.TryGetValue(spec.fieldKey, out var list))
        {
            list = new List<AllocationSpec>();
            byField[spec.fieldKey] = list;
        }
        list.Add(spec);
    }

    private static bool AllocationOverlapsSlots(
        Dictionary<string, List<(int startMin, int endMin)>> slotRanges,
        string fieldKey,
        DateOnly dateFrom,
        DateOnly dateTo,
        int startMin,
        int endMin,
        HashSet<DayOfWeek> days)
    {
        for (var date = dateFrom; date <= dateTo; date = date.AddDays(1))
        {
            if (days.Count > 0 && !days.Contains(date.DayOfWeek)) continue;
            var key = SlotOverlap.BuildRangeKey(fieldKey, date);
            if (SlotOverlap.HasOverlap(slotRanges, key, startMin, endMin)) return true;
        }
        return false;
    }

    private static async Task<Dictionary<string, List<(int startMin, int endMin)>>> LoadExistingSlotRangesAsync(
        List<string[]> rows,
        Dictionary<string, int> headerIndex,
        TableClient slotsTable,
        string leagueId)
    {
        DateOnly? minDate = null;
        DateOnly? maxDate = null;
        var fieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (CsvMini.IsBlankRow(r)) continue;

            var dateFromRaw = CsvMini.Get(r, headerIndex, "datefrom").Trim();
            var dateToRaw = CsvMini.Get(r, headerIndex, "dateto").Trim();
            if (TryNormalizeDate(dateFromRaw, out var dFromNorm) &&
                DateOnly.TryParseExact(dFromNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dFrom))
            {
                minDate = minDate is null || dFrom < minDate.Value ? dFrom : minDate.Value;
            }
            if (TryNormalizeDate(dateToRaw, out var dToNorm) &&
                DateOnly.TryParseExact(dToNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dTo))
            {
                maxDate = maxDate is null || dTo > maxDate.Value ? dTo : maxDate.Value;
            }

            var fieldKeyRaw = CsvMini.Get(r, headerIndex, "fieldkey").Trim();
            if (TryParseFieldKey(fieldKeyRaw, out var parkCode, out var fieldCode))
            {
                fieldKeys.Add($"{parkCode}/{fieldCode}");
            }
        }

        if (minDate is null || maxDate is null || fieldKeys.Count == 0)
            return new Dictionary<string, List<(int startMin, int endMin)>>(StringComparer.OrdinalIgnoreCase);

        var prefix = $"SLOT|{leagueId}|";
        var next = prefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}' " +
                     $"and GameDate ge '{minDate:yyyy-MM-dd}' and GameDate le '{maxDate:yyyy-MM-dd}'";

        var existing = new Dictionary<string, List<(int startMin, int endMin)>>(StringComparer.OrdinalIgnoreCase);
        await foreach (var e in slotsTable.QueryAsync<TableEntity>(filter: filter))
        {
            var status = (e.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                continue;

            var isAvailability = e.GetBoolean("IsAvailability") ?? false;
            if (isAvailability) continue;

            var fieldKey = (e.GetString("FieldKey") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fieldKey) || !fieldKeys.Contains(fieldKey))
                continue;

            var gameDateRaw = (e.GetString("GameDate") ?? "").Trim();
            if (!TryNormalizeDate(gameDateRaw, out var gameDateNorm) ||
                !DateOnly.TryParseExact(gameDateNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var gameDate)) continue;

            var startTime = (e.GetString("StartTime") ?? "").Trim();
            var endTime = (e.GetString("EndTime") ?? "").Trim();
            var startMin = SlotOverlap.ParseMinutes(startTime);
            var endMin = SlotOverlap.ParseMinutes(endTime);
            if (startMin < 0 || endMin <= startMin) continue;

            var key = SlotOverlap.BuildRangeKey(fieldKey, gameDate);
            SlotOverlap.AddRange(existing, key, startMin, endMin);
        }

        return existing;
    }

    private static async Task<HashSet<string>> LoadFieldKeysAsync(TableClient fieldsTable, string leagueId)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pkPrefix = $"FIELD|{leagueId}|";
        var next = pkPrefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

        await foreach (var e in fieldsTable.QueryAsync<TableEntity>(filter: filter))
        {
            var parkCode = ExtractParkCodeFromPk(e.PartitionKey, leagueId);
            var fieldCode = e.RowKey;
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode)) continue;
            keys.Add($"{parkCode}/{fieldCode}");
        }

        return keys;
    }

    private static async Task<(Dictionary<string, List<AllocationSpec>> byField, HashSet<string> exactKeys)>
        LoadExistingAllocationsAsync(TableClient table, string leagueId)
    {
        var byField = new Dictionary<string, List<AllocationSpec>>(StringComparer.OrdinalIgnoreCase);
        var exactKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pkPrefix = $"ALLOC|{leagueId}|";
        var next = pkPrefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            var isActive = e.GetBoolean("IsActive") ?? true;
            if (!isActive) continue;

            var fieldKey = (e.GetString("FieldKey") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fieldKey)) continue;

            var startsOnRaw = (e.GetString("StartsOn") ?? "").Trim();
            var endsOnRaw = (e.GetString("EndsOn") ?? "").Trim();
            if (!TryNormalizeDate(startsOnRaw, out var startsOnNorm) ||
                !DateOnly.TryParseExact(startsOnNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateFrom)) continue;
            if (!TryNormalizeDate(endsOnRaw, out var endsOnNorm) ||
                !DateOnly.TryParseExact(endsOnNorm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTo)) continue;

            var startTime = (e.GetString("StartTimeLocal") ?? "").Trim();
            var endTime = (e.GetString("EndTimeLocal") ?? "").Trim();
            var startMin = SlotOverlap.ParseMinutes(startTime);
            var endMin = SlotOverlap.ParseMinutes(endTime);
            if (startMin < 0 || endMin <= startMin) continue;

            var daysRaw = (e.GetString("DaysOfWeek") ?? "").Trim();
            if (!TryParseDays(daysRaw, out var days)) continue;

            var spec = new AllocationSpec(fieldKey, dateFrom, dateTo, startMin, endMin, days);
            AddAllocation(byField, spec);
            exactKeys.Add(BuildAllocationKey(fieldKey, dateFrom, dateTo, startTime, endTime, days));
        }

        return (byField, exactKeys);
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

    private static string ExtractParkCodeFromPk(string pk, string leagueId)
    {
        var prefix = $"FIELD|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }

    private static bool TryNormalizeDate(string raw, out string normalized)
    {
        normalized = "";
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;

        var formats = new[] { "yyyy-MM-dd", "M/d/yyyy", "M-d-yyyy", "MM/dd/yyyy", "MM-dd-yyyy" };
        if (!DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }
}
