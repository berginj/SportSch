using System.Globalization;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class ClearDivisionSlots
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public ClearDivisionSlots(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<ClearDivisionSlots>();
    }

    public record ClearDivisionSlotsRequest(
        string? division,
        string? dateFrom,
        string? dateTo,
        string? fieldKey,
        bool? includeAvailability,
        bool? dryRun
    );

    [Function("ClearDivisionSlots")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "slots/clear")] HttpRequestData req)
    {
        var stage = "init";
        try
        {
            stage = "auth";
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            stage = "read_body";
            var body = await HttpUtil.ReadJsonAsync<ClearDivisionSlotsRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");

            var division = (body.division ?? "").Trim();
            var dateFromRaw = (body.dateFrom ?? "").Trim();
            var dateToRaw = (body.dateTo ?? "").Trim();
            var fieldKey = (body.fieldKey ?? "").Trim();
            var includeAvailability = body.includeAvailability ?? false;
            var dryRun = body.dryRun ?? false;

            if (string.IsNullOrWhiteSpace(division))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "division is required.");
            ApiGuards.EnsureValidTableKeyPart("division", division);

            DateOnly? fromDate = null;
            DateOnly? toDate = null;

            if (!string.IsNullOrWhiteSpace(dateFromRaw))
            {
                if (!DateOnly.TryParseExact(dateFromRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "dateFrom must be YYYY-MM-DD.");
                fromDate = parsedFrom;
            }

            if (!string.IsNullOrWhiteSpace(dateToRaw))
            {
                if (!DateOnly.TryParseExact(dateToRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "dateTo must be YYYY-MM-DD.");
                toDate = parsedTo;
            }

            if (fromDate.HasValue && toDate.HasValue && toDate.Value < fromDate.Value)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "dateTo must be on or after dateFrom.");

            stage = "query_slots";
            var slotsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
            var slotRequestsTable = await TableClients.GetTableAsync(_svc, Constants.Tables.SlotRequests);
            var pk = Constants.Pk.Slots(leagueId, division);
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";

            var scanned = 0;
            var matched = 0;
            var deleted = 0;
            var slotRequestsDeleted = 0;
            var deleteErrors = 0;

            stage = "scan_delete";
            await foreach (var slot in slotsTable.QueryAsync<TableEntity>(filter: filter))
            {
                scanned++;

                var isAvailability = SlotEntityUtil.IsAvailability(slot);
                if (!includeAvailability && isAvailability) continue;

                if (!MatchesField(slot, fieldKey)) continue;
                if (!MatchesDateRange(slot, fromDate, toDate)) continue;

                matched++;
                if (dryRun) continue;

                try
                {
                    await slotsTable.DeleteEntityAsync(slot.PartitionKey, slot.RowKey, ETag.All);
                    deleted++;

                    var slotId = (SlotEntityUtil.ReadString(slot, "SlotId", slot.RowKey) ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(slotId)) continue;

                    var slotRequestPk = Constants.Pk.SlotRequests(leagueId, division, slotId);
                    var slotRequestFilter = $"PartitionKey eq '{ApiGuards.EscapeOData(slotRequestPk)}'";
                    await foreach (var slotRequest in slotRequestsTable.QueryAsync<TableEntity>(filter: slotRequestFilter))
                    {
                        await slotRequestsTable.DeleteEntityAsync(slotRequest.PartitionKey, slotRequest.RowKey, ETag.All);
                        slotRequestsDeleted++;
                    }
                }
                catch (RequestFailedException ex)
                {
                    deleteErrors++;
                    _log.LogWarning(ex, "ClearDivisionSlots failed deleting slot {PartitionKey}/{RowKey}", slot.PartitionKey, slot.RowKey);
                }
            }

            return ApiResponses.Ok(req, new
            {
                leagueId,
                division,
                fieldKey,
                includeAvailability,
                dryRun,
                dateFrom = fromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                dateTo = toDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                scanned,
                matched,
                deleted,
                slotRequestsDeleted,
                deleteErrors
            });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (RequestFailedException ex)
        {
            _log.LogError(ex, "ClearDivisionSlots storage request failed at stage {Stage}", stage);
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(req, HttpStatusCode.BadGateway, "STORAGE_ERROR", "Storage request failed",
                new { requestId, stage, status = ex.Status, code = ex.ErrorCode, detail = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ClearDivisionSlots failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error",
                new { requestId, stage, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    private static bool MatchesField(TableEntity slot, string requestedFieldKey)
    {
        if (string.IsNullOrWhiteSpace(requestedFieldKey)) return true;
        var rowFieldKey = SlotEntityUtil.ReadString(slot, "FieldKey");
        return string.Equals(rowFieldKey, requestedFieldKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDateRange(TableEntity slot, DateOnly? fromDate, DateOnly? toDate)
    {
        if (!fromDate.HasValue && !toDate.HasValue) return true;
        var gameDateRaw = SlotEntityUtil.ReadString(slot, "GameDate");
        if (!DateOnly.TryParseExact(gameDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var gameDate))
            return false;

        if (fromDate.HasValue && gameDate < fromDate.Value) return false;
        if (toDate.HasValue && gameDate > toDate.Value) return false;
        return true;
    }

}
