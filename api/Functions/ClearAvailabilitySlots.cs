using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Globalization;

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
        string? fieldKey
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
            var filter = "";
            if (!string.IsNullOrWhiteSpace(division))
            {
                var pk = Constants.Pk.Slots(leagueId, division);
                filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
            }
            else
            {
                var prefix = $"SLOT|{leagueId}|";
                var next = prefix + "\uffff";
                filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
            }

            var deleted = 0;
            stage = "scan_delete";
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                if (!SlotEntityUtil.IsAvailability(e))
                    continue;

                var gameDateRaw = SlotEntityUtil.ReadString(e, "GameDate");
                if (!DateOnly.TryParseExact(gameDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var gameDate))
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

            return ApiResponses.Ok(req, new { leagueId, division, dateFrom, dateTo, fieldKey, deleted });
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
}
