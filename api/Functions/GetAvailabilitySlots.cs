using System.Globalization;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class GetAvailabilitySlots
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public GetAvailabilitySlots(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<GetAvailabilitySlots>();
        _svc = tableServiceClient;
    }

    [Function("GetAvailabilitySlots")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "availability-slots")] HttpRequestData req)
    {
        var stage = "init";
        try
        {
            stage = "auth";
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            stage = "read_query";
            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();
            var fieldKey = (ApiGuards.GetQueryParam(req, "fieldKey") ?? "").Trim();
            var dateFromRaw = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateToRaw = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(division))
                ApiGuards.EnsureValidTableKeyPart("division", division);

            DateOnly? dateFrom = null;
            DateOnly? dateTo = null;
            if (!string.IsNullOrWhiteSpace(dateFromRaw))
            {
                if (!DateOnly.TryParseExact(dateFromRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "dateFrom must be YYYY-MM-DD.");
                dateFrom = parsedFrom;
            }

            if (!string.IsNullOrWhiteSpace(dateToRaw))
            {
                if (!DateOnly.TryParseExact(dateToRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "dateTo must be YYYY-MM-DD.");
                dateTo = parsedTo;
            }

            if (dateFrom.HasValue && dateTo.HasValue && dateTo.Value < dateFrom.Value)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "dateTo must be on or after dateFrom.");

            stage = "query_slots";
            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Slots);
            string filter;
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

            stage = "scan";
            var matches = new List<TableEntity>();
            await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
            {
                if (!IsAvailability(entity))
                    continue;

                if (!string.IsNullOrWhiteSpace(fieldKey))
                {
                    var rowFieldKey = ReadString(entity, "FieldKey");
                    if (!string.Equals(rowFieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var status = ReadString(entity, "Status", Constants.Status.SlotOpen);
                if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                    continue;

                var gameDateRaw = ReadString(entity, "GameDate");
                if (!DateOnly.TryParseExact(gameDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var gameDate))
                    continue;

                if (dateFrom.HasValue && gameDate < dateFrom.Value)
                    continue;
                if (dateTo.HasValue && gameDate > dateTo.Value)
                    continue;

                matches.Add(entity);
            }

            var sorted = matches
                .OrderBy(e => ReadString(e, "GameDate"))
                .ThenBy(e => ReadString(e, "StartTime"))
                .ThenBy(e => ReadString(e, "DisplayName"))
                .Select(EntityMappers.MapSlot)
                .ToList();

            return ApiResponses.Ok(req, new { items = sorted, count = sorted.Count });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            _log.LogError(ex, "GetAvailabilitySlots storage request failed at stage {Stage}", stage);
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(req, HttpStatusCode.BadGateway, "STORAGE_ERROR", "Storage request failed",
                new { requestId, stage, status = ex.Status, code = ex.ErrorCode, detail = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetAvailabilitySlots failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "Internal Server Error",
                new { requestId, stage, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    private static bool IsAvailability(TableEntity entity)
    {
        if (ReadBool(entity, "IsAvailability", false))
            return true;

        var gameType = ReadString(entity, "GameType");
        return string.Equals(gameType, "Availability", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadString(TableEntity entity, string key, string defaultValue = "")
    {
        if (!entity.TryGetValue(key, out var value) || value is null) return defaultValue;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? defaultValue : text.Trim();
    }

    private static bool ReadBool(TableEntity entity, string key, bool defaultValue)
    {
        if (!entity.TryGetValue(key, out var value) || value is null) return defaultValue;
        if (value is bool b) return b;
        var text = value.ToString()?.Trim() ?? "";
        if (bool.TryParse(text, out var parsed)) return parsed;
        if (int.TryParse(text, out var parsedInt)) return parsedInt != 0;
        return defaultValue;
    }
}
