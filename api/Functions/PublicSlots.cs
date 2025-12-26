using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class PublicSlots
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string SlotsTableName = Constants.Tables.Slots;

    public PublicSlots(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<PublicSlots>();
        _svc = tableServiceClient;
    }

    public record PublicSlotDto(
        string slotId,
        string leagueId,
        string leagueName,
        string division,
        string offeringTeamId,
        string gameDate,
        string startTime,
        string endTime,
        string parkName,
        string fieldName,
        string displayName,
        string fieldKey,
        string status,
        DateTimeOffset createdUtc
    );

    [Function("PublicSlots")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public/slots")] HttpRequestData req)
    {
        try
        {
            var limitRaw = (ApiGuards.GetQueryParam(req, "limit") ?? "").Trim();
            var limit = 6;
            if (int.TryParse(limitRaw, out var parsed))
                limit = Math.Clamp(parsed, 1, 20);

            var table = await TableClients.GetTableAsync(_svc, SlotsTableName);
            var filter = $"Status eq '{ApiGuards.EscapeOData(Constants.Status.SlotOpen)}' and IsExternalOffer eq true";

            var list = new List<PublicSlotDto>();

            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var slotId = e.RowKey;
                var leagueId = (e.GetString("LeagueId") ?? "").Trim();
                var division = e.GetString("Division") ?? "";

                var isAvailability = e.GetBoolean("IsAvailability") ?? false;
                if (isAvailability) continue;

                var offeringTeamId = e.GetString("OfferingTeamId") ?? "";
                var gameDate = e.GetString("GameDate") ?? "";
                var startTime = e.GetString("StartTime") ?? "";
                var endTime = e.GetString("EndTime") ?? "";

                var parkName = e.GetString("ParkName") ?? "";
                var fieldName = e.GetString("FieldName") ?? "";
                var displayName = e.GetString("DisplayName") ?? (string.IsNullOrWhiteSpace(parkName) || string.IsNullOrWhiteSpace(fieldName) ? "" : $"{parkName} > {fieldName}");
                var fieldKey = e.GetString("FieldKey") ?? "";

                var status = e.GetString("Status") ?? Constants.Status.SlotOpen;
                var createdUtc = e.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.MinValue;

                list.Add(new PublicSlotDto(
                    slotId: slotId,
                    leagueId: leagueId,
                    leagueName: "",
                    division: division,
                    offeringTeamId: offeringTeamId,
                    gameDate: gameDate,
                    startTime: startTime,
                    endTime: endTime,
                    parkName: parkName,
                    fieldName: fieldName,
                    displayName: displayName,
                    fieldKey: fieldKey,
                    status: status,
                    createdUtc: createdUtc
                ));
            }

            var ordered = list
                .OrderByDescending(x => x.createdUtc)
                .ThenByDescending(x => x.gameDate)
                .Take(limit)
                .ToList();

            if (ordered.Count > 0)
            {
                var leaguesTable = await TableClients.GetTableAsync(_svc, Constants.Tables.Leagues);
                var leagueNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var slot in ordered)
                {
                    if (string.IsNullOrWhiteSpace(slot.leagueId) || leagueNames.ContainsKey(slot.leagueId))
                        continue;

                    try
                    {
                        var entity = (await leaguesTable.GetEntityAsync<TableEntity>(Constants.Pk.Leagues, slot.leagueId)).Value;
                        var name = (entity.GetString("Name") ?? slot.leagueId).Trim();
                        leagueNames[slot.leagueId] = name;
                    }
                    catch (RequestFailedException ex) when (ex.Status == 404)
                    {
                        leagueNames[slot.leagueId] = slot.leagueId;
                    }
                }

                ordered = ordered
                    .Select(slot => slot with { leagueName = leagueNames.TryGetValue(slot.leagueId, out var name) ? name : slot.leagueId })
                    .ToList();
            }

            return ApiResponses.Ok(req, ordered);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PublicSlots failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
