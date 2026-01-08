using System.Net;
using System.Text;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

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
                    "Missing required columns. Required: fieldKey, dateFrom, dateTo, startTime, endTime. Optional: division, daysOfWeek, notes, isActive.",
                    new { headerPreview, missing });
            }

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityAllocations);

            int upserted = 0, rejected = 0, skipped = 0;
            var errors = new List<object>();
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
                if (!DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out _))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "dateFrom must be YYYY-MM-DD." });
                    continue;
                }
                if (!DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out _))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, error = "dateTo must be YYYY-MM-DD." });
                    continue;
                }

                var scope = string.IsNullOrWhiteSpace(divisionRaw) ? ScopeLeague : divisionRaw;
                if (!string.Equals(scope, ScopeLeague, StringComparison.OrdinalIgnoreCase))
                {
                    ApiGuards.EnsureValidTableKeyPart("division", scope);
                }

                var pk = Constants.Pk.FieldAvailabilityAllocations(leagueId, scope, $"{parkCode}/{fieldCode}");
                var allocationId = Guid.NewGuid().ToString("N");
                var daysList = NormalizeDays(daysRaw);
                var isActive = !string.IsNullOrWhiteSpace(isActiveRaw) ? bool.TryParse(isActiveRaw, out var b) && b : true;

                var entity = new TableEntity(pk, allocationId)
                {
                    ["LeagueId"] = leagueId,
                    ["Scope"] = scope,
                    ["FieldKey"] = $"{parkCode}/{fieldCode}",
                    ["Division"] = scope,
                    ["StartsOn"] = dateFrom,
                    ["EndsOn"] = dateTo,
                    ["DaysOfWeek"] = string.Join(",", daysList),
                    ["StartTimeLocal"] = startTime,
                    ["EndTimeLocal"] = endTime,
                    ["Notes"] = notes,
                    ["IsActive"] = isActive,
                    ["UpdatedUtc"] = DateTimeOffset.UtcNow
                };

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

            return ApiResponses.Ok(req, new { leagueId, upserted, rejected, skipped, errors });
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
                var days = (e.GetString("DaysOfWeek") ?? "")
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
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

            var scope = (body.scope ?? ScopeLeague).Trim();
            if (!string.Equals(scope, ScopeLeague, StringComparison.OrdinalIgnoreCase))
                ApiGuards.EnsureValidTableKeyPart("division", scope);

            var dateFrom = (body.dateFrom ?? "").Trim();
            var dateTo = (body.dateTo ?? "").Trim();
            var fieldKey = (body.fieldKey ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(dateFrom) && !DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!string.IsNullOrWhiteSpace(dateTo) && !DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityAllocations);
            var pkPrefix = $"ALLOC|{leagueId}|{scope}|";
            var next = pkPrefix + "\uffff";
            var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
            if (!string.IsNullOrWhiteSpace(fieldKey))
                filter += $" and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";

            var deleted = 0;
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var startsOn = e.GetString("StartsOn") ?? "";
                var endsOn = e.GetString("EndsOn") ?? "";
                if (!string.IsNullOrWhiteSpace(dateFrom) && string.CompareOrdinal(endsOn, dateFrom) < 0) continue;
                if (!string.IsNullOrWhiteSpace(dateTo) && string.CompareOrdinal(startsOn, dateTo) > 0) continue;
                await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, e.ETag);
                deleted++;
            }

            return ApiResponses.Ok(req, new { leagueId, scope, dateFrom, dateTo, fieldKey, deleted });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ClearAvailabilityAllocations failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static List<string> NormalizeDays(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Length > 3 ? x.Substring(0, 3) : x)
            .ToList();
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
}
