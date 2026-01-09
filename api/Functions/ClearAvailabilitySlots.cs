using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

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
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<ClearAvailabilitySlotsRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            var dateFrom = (body.dateFrom ?? "").Trim();
            var dateTo = (body.dateTo ?? "").Trim();
            var fieldKey = (body.fieldKey ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(division))
                ApiGuards.EnsureValidTableKeyPart("division", division);

            if (!DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateFrom must be YYYY-MM-DD.");
            if (!DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "dateTo must be YYYY-MM-DD.");

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
                filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(prefix + "~")}'";
            }

            filter += $" and GameDate ge '{ApiGuards.EscapeOData(dateFrom)}' and GameDate le '{ApiGuards.EscapeOData(dateTo)}' " +
                      $"and IsAvailability eq true";

            if (!string.IsNullOrWhiteSpace(fieldKey))
                filter += $" and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";

            var deleted = 0;
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var status = (e.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
                if (!string.Equals(status, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
                    continue;

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
        catch (Exception ex)
        {
            _log.LogError(ex, "ClearAvailabilitySlots failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
