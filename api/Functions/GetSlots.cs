using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class GetSlots
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string SlotsTableName = Constants.Tables.Slots;

    public GetSlots(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<GetSlots>();
        _svc = tableServiceClient;
    }

    public record SlotDto(
        string slotId,
        string leagueId,
        string division,
        string offeringTeamId,
        string confirmedTeamId,
        string gameDate,
        string startTime,
        string endTime,
        string parkName,
        string fieldName,
        string displayName,
        string fieldKey,
        string gameType,
        string status,
        string notes
    );

    [Function("GetSlots")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "slots")] HttpRequestData req)
    {
        try
        {
            // League scoping is header-only
            var leagueId = ApiGuards.RequireLeagueId(req);

            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();
            var statusFilter = (ApiGuards.GetQueryParam(req, "status") ?? "").Trim();
            var dateFrom = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateTo = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();

            var table = await TableClients.GetTableAsync(_svc, SlotsTableName);
            // Partitioning is SLOT#{leagueId}#{division}
            // If division is omitted, query all slot partitions for this league by prefix range.
            var filter = "";
            if (!string.IsNullOrWhiteSpace(division))
            {
                var pk = $"SLOT#{leagueId}#{division}";
                filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
            }
            else
            {
                var prefix = $"SLOT#{leagueId}#";
                // lexicographic prefix range: [prefix, prefix + '~')
                filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(prefix + "~")}'";
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
                filter += $" and Status eq '{ApiGuards.EscapeOData(statusFilter)}'";

            if (!string.IsNullOrWhiteSpace(dateFrom))
                filter += $" and GameDate ge '{ApiGuards.EscapeOData(dateFrom)}'";
            if (!string.IsNullOrWhiteSpace(dateTo))
                filter += $" and GameDate le '{ApiGuards.EscapeOData(dateTo)}'";

            var list = new List<SlotDto>();

            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var slotId = e.RowKey;

                var offeringTeamId = e.GetString("OfferingTeamId") ?? "";
                var gameDate = e.GetString("GameDate") ?? "";
                var startTime = e.GetString("StartTime") ?? "";
                var endTime = e.GetString("EndTime") ?? "";

                var parkName = e.GetString("ParkName") ?? "";
                var fieldName = e.GetString("FieldName") ?? "";
                var displayName = e.GetString("DisplayName") ?? (string.IsNullOrWhiteSpace(parkName) || string.IsNullOrWhiteSpace(fieldName) ? "" : $"{parkName} > {fieldName}");
                var fieldKey = e.GetString("FieldKey") ?? "";

                var gameType = e.GetString("GameType") ?? "Swap";
                var status = e.GetString("Status") ?? Constants.Status.SlotOpen;

                // Default behavior (when no explicit status filter is provided):
                // return Open + Confirmed only. Cancelled is only returned when explicitly requested.
                if (string.IsNullOrWhiteSpace(statusFilter) && status == Constants.Status.SlotCancelled)
                    continue;
                var notes = e.GetString("Notes") ?? "";
                var confirmedTeamId = e.GetString("ConfirmedTeamId") ?? "";

                list.Add(new SlotDto(
                    slotId: slotId,
                    leagueId: leagueId,
                    division: e.GetString("Division") ?? division,
                    offeringTeamId: offeringTeamId,
                    confirmedTeamId: confirmedTeamId,
                    gameDate: gameDate,
                    startTime: startTime,
                    endTime: endTime,
                    parkName: parkName,
                    fieldName: fieldName,
                    displayName: displayName,
                    fieldKey: fieldKey,
                    gameType: gameType,
                    status: status,
                    notes: notes
                ));
            }

            return ApiResponses.Ok(req, list.OrderBy(x => x.gameDate).ThenBy(x => x.startTime).ThenBy(x => x.displayName));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetSlots failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
