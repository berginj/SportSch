using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using System.Linq;

namespace GameSwap.Functions.Functions;

public class GetSlotRequests
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string RequestsTableName = Constants.Tables.SlotRequests;
    private const string SlotsTableName = Constants.Tables.Slots;

    public GetSlotRequests(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<GetSlotRequests>();
        _svc = tableServiceClient;
    }

    [Function("GetSlotRequests")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "slots/{division}/{slotId}/requests")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            // Validate slot exists (cheap guardrail)
            var slots = await TableClients.GetTableAsync(_svc, SlotsTableName);
            var slotPk = $"SLOT|{leagueId}|{division}";
            try { _ = (await slots.GetEntityAsync<TableEntity>(slotPk, slotId)).Value; }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Slot not found");
            }

            var table = await TableClients.GetTableAsync(_svc, RequestsTableName);
            var pk = $"SLOTREQ|{leagueId}|{division}|{slotId}";
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";

            var list = new List<object>();
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                list.Add(new
                {
                    requestId = e.RowKey,
                    requestingTeamId = e.GetString("RequestingTeamId") ?? "",
                    requestingEmail = e.GetString("RequestingEmail") ?? "",
                    notes = e.GetString("Notes") ?? "",
                    status = e.GetString("Status") ?? "Pending",
                    requestedUtc = e.GetDateTimeOffset("RequestedUtc")
                });
            }

            return ApiResponses.Ok(req, list.OrderByDescending(x => ((dynamic)x).requestedUtc ?? DateTimeOffset.MinValue));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetSlotRequests failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
