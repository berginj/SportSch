using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Linq;

namespace GameSwap.Functions.Functions;

public class ClearAvailabilitySlots
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public ClearAvailabilitySlots(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<ClearAvailabilitySlots>();
        _svc = tableServiceClient;
    }

    public record ClearAvailabilitySlotsRequest(
        string? division,
        string? dateFrom,
        string? dateTo,
        string? fieldKey,
        bool? conflictsOnly
    );

    [Function("ClearAvailabilitySlots")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "availability-slots/clear")] HttpRequestData req)
    {
        var stage = "init";
        try
        {
            stage = "auth";
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            stage = "read_body";
            var body = await HttpUtil.ReadJsonAsync<ClearAvailabilitySlotsRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            var dateFrom = (body.dateFrom ?? "").Trim();
            var dateTo = (body.dateTo ?? "").Trim();
            var fieldKey = (body.fieldKey ?? "").Trim();
            var conflictsOnly = body.conflictsOnly ?? false;

            if (!string.IsNullOrWhiteSpace(division))
                ApiGuards.EnsureValidTableKeyPart("division", division);

            if (!DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");
            if (toDate < fromDate)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be on or after dateFrom.");

            stage = "query_slots";
            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
            if (!conflictsOnly)
            {
                var filter = BuildSlotQueryFilter(leagueId, division);

                var deleted = 0;
                stage = "scan_delete";
                await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
                {
                    if (!SlotEntityUtil.IsAvailability(e))
                        continue;

                    if (!TryReadSlotDate(e, out var gameDate))
                        continue;
                    if (gameDate < fromDate || gameDate > toDate)
                        continue;

                    if (!string.IsNullOrWhiteSpace(fieldKey))
                    {
                        var rowFieldKey = SlotEntityUtil.ReadString(e, "FieldKey");
                        if (!string.Equals(rowFieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    try
                    {
                        await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, e.ETag);
                        deleted++;
                    }
                    catch (RequestFailedException ex)
                    {
                        _log.LogWarning(ex, "ClearAvailabilitySlots failed deleting {pk}/{rk}", e.PartitionKey, e.RowKey);
                    }
                }

                return ApiResponses.Ok(req, new { leagueId, division, dateFrom, dateTo, fieldKey, deleted, conflictsOnly = false });
            }

            stage = "scan_conflicts";
            var allLeagueFilter = BuildSlotQueryFilter(leagueId, division: "");
            var buckets = new Dictionary<string, List<SlotRange>>(StringComparer.OrdinalIgnoreCase);
            var targetAvailability = new Dictionary<string, TableEntity>(StringComparer.OrdinalIgnoreCase);
            var scanned = 0;
            var invalidRangeSkipped = 0;
            var cancelledSkipped = 0;

            await foreach (var e in table.QueryAsync<TableEntity>(filter: allLeagueFilter))
            {
                scanned++;

                if (!TryReadSlotDate(e, out var gameDate)) continue;
                if (gameDate < fromDate || gameDate > toDate) continue;

                var rowFieldKey = SlotEntityUtil.ReadString(e, "FieldKey");
                if (string.IsNullOrWhiteSpace(rowFieldKey)) continue;
                if (!string.IsNullOrWhiteSpace(fieldKey) &&
                    !string.Equals(rowFieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryReadSlotRange(e, out var startMin, out var endMin))
                {
                    invalidRangeSkipped++;
                    continue;
                }

                var isAvailability = SlotEntityUtil.IsAvailability(e);
                var status = SlotEntityUtil.ReadString(e, "Status", Constants.Status.SlotOpen);
                var isCancelled = string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase);
                if (isCancelled) cancelledSkipped++;

                var key = BucketKey(rowFieldKey, gameDate);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<SlotRange>();
                    buckets[key] = list;
                }

                var slotKey = SlotKey(e);
                list.Add(new SlotRange(slotKey, e.PartitionKey, e.RowKey, rowFieldKey, gameDate, startMin, endMin, isAvailability, isCancelled));

                if (isAvailability && MatchesDivision(e, leagueId, division))
                {
                    targetAvailability[slotKey] = e;
                }
            }

            var conflictAssigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var conflictAvailability = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var bucket in buckets.Values)
            {
                var active = bucket
                    .Where(s => !s.IsCancelled)
                    .OrderBy(s => s.StartMin)
                    .ThenBy(s => s.EndMin)
                    .ThenBy(s => s.RowKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (active.Count <= 1) continue;

                var nonAvailability = active.Where(s => !s.IsAvailability).ToList();
                var availability = active.Where(s => s.IsAvailability).ToList();

                foreach (var a in availability)
                {
                    if (!targetAvailability.ContainsKey(a.SlotKey)) continue;
                    if (nonAvailability.Any(o => Overlaps(a, o)))
                    {
                        conflictAssigned.Add(a.SlotKey);
                    }
                }

                for (var i = 0; i < availability.Count; i++)
                {
                    var current = availability[i];
                    for (var j = i + 1; j < availability.Count; j++)
                    {
                        var next = availability[j];
                        if (next.StartMin >= current.EndMin) break;
                        if (!Overlaps(current, next)) continue;

                        // Keep the earlier slot in the sorted order and mark later overlaps for removal.
                        if (targetAvailability.ContainsKey(next.SlotKey))
                        {
                            conflictAvailability.Add(next.SlotKey);
                        }
                    }
                }
            }

            var toDelete = conflictAssigned
                .Concat(conflictAvailability)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var deletedConflicts = 0;
            var deleteErrors = 0;
            stage = "delete_conflicts";
            foreach (var slotKey in toDelete)
            {
                if (!targetAvailability.TryGetValue(slotKey, out var entity)) continue;
                try
                {
                    await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag);
                    deletedConflicts++;
                }
                catch (RequestFailedException ex)
                {
                    deleteErrors++;
                    _log.LogWarning(ex, "ClearAvailabilitySlots conflictsOnly failed deleting {pk}/{rk}", entity.PartitionKey, entity.RowKey);
                }
            }

            return ApiResponses.Ok(req, new
            {
                leagueId,
                division,
                dateFrom,
                dateTo,
                fieldKey,
                conflictsOnly = true,
                scanned,
                targetAvailability = targetAvailability.Count,
                conflictWithAssigned = conflictAssigned.Count,
                conflictWithAvailability = conflictAvailability.Count,
                conflictsFound = toDelete.Count,
                deleted = deletedConflicts,
                deleteErrors,
                invalidRangeSkipped,
                cancelledSkipped
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            _log.LogError(ex, "ClearAvailabilitySlots storage request failed at stage {Stage}", stage);
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(req, HttpStatusCode.BadGateway, "STORAGE_ERROR", "Storage request failed",
                new { requestId, stage, status = ex.Status, code = ex.ErrorCode, detail = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ClearAvailabilitySlots failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error",
                new { requestId, stage, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    private static string BuildSlotQueryFilter(string leagueId, string? division)
    {
        if (!string.IsNullOrWhiteSpace(division))
        {
            var pk = Constants.Pk.Slots(leagueId, division);
            return $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
        }

        var prefix = $"SLOT|{leagueId}|";
        var next = prefix + "\uffff";
        return $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
    }

    private static bool MatchesDivision(TableEntity e, string leagueId, string division)
    {
        if (string.IsNullOrWhiteSpace(division)) return true;
        return string.Equals(e.PartitionKey, Constants.Pk.Slots(leagueId, division), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadSlotDate(TableEntity e, out DateOnly gameDate)
    {
        gameDate = default;
        var raw = SlotEntityUtil.ReadString(e, "GameDate");
        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out gameDate);
    }

    private static bool TryReadSlotRange(TableEntity e, out int startMin, out int endMin)
    {
        startMin = 0;
        endMin = 0;

        if (TryReadMinuteValue(e, "StartMin", out var parsedStart) &&
            TryReadMinuteValue(e, "EndMin", out var parsedEnd) &&
            parsedEnd > parsedStart)
        {
            startMin = parsedStart;
            endMin = parsedEnd;
            return true;
        }

        var startTime = SlotEntityUtil.ReadString(e, "StartTime");
        var endTime = SlotEntityUtil.ReadString(e, "EndTime");
        if (!TimeUtil.TryParseMinutes(startTime, out startMin)) return false;
        if (!TimeUtil.TryParseMinutes(endTime, out endMin)) return false;
        return endMin > startMin;
    }

    private static bool TryReadMinuteValue(TableEntity e, string key, out int value)
    {
        value = 0;
        if (!e.TryGetValue(key, out var raw) || raw is null) return false;
        if (raw is int i)
        {
            value = i;
            return true;
        }

        return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string SlotKey(TableEntity e) => $"{e.PartitionKey}|{e.RowKey}";
    private static string BucketKey(string fieldKey, DateOnly gameDate) => $"{fieldKey}|{gameDate:yyyy-MM-dd}";

    private static bool Overlaps(SlotRange a, SlotRange b)
        => a.StartMin < b.EndMin && a.EndMin > b.StartMin;

    private record SlotRange(
        string SlotKey,
        string PartitionKey,
        string RowKey,
        string FieldKey,
        DateOnly GameDate,
        int StartMin,
        int EndMin,
        bool IsAvailability,
        bool IsCancelled);
}
