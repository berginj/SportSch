using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Functions;

public class AdminWipe
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public AdminWipe(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<AdminWipe>();
        _svc = tableServiceClient;
    }

    public record WipeReq(List<string>? tables, string? confirm);

    [Function("AdminWipeLeagueData")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/wipe")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me);

            var body = await HttpUtil.ReadJsonAsync<WipeReq>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");
            if (!string.Equals(body.confirm, "WIPE", StringComparison.OrdinalIgnoreCase))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Missing confirm=WIPE");

            var requested = (body.tables ?? new List<string>())
                .Select(x => (x ?? "").Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var defaultTables = new List<string>
            {
                "accessrequests",
                "divisions",
                "events",
                "fields",
                "invites",
                "memberships",
                "slotrequests",
                "slots",
                "teams"
            };

            var targets = requested.Count == 0 ? defaultTables : requested;
            var results = new List<object>();

            foreach (var key in targets)
            {
                try
                {
                    var deleted = await WipeTableAsync(key, leagueId);
                    results.Add(new { table = key, deleted, skipped = false });
                }
                catch (InvalidOperationException ex)
                {
                    results.Add(new { table = key, deleted = 0, skipped = true, error = ex.Message });
                }
                catch (RequestFailedException ex)
                {
                    _log.LogError(ex, "Admin wipe failed for {table}", key);
                    results.Add(new { table = key, deleted = 0, skipped = true, error = ex.Message });
                }
            }

            return ApiResponses.Ok(req, new { leagueId, results });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "AdminWipe failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<int> WipeTableAsync(string key, string leagueId)
    {
        return key switch
        {
            "accessrequests" => await DeleteByFilterAsync(Constants.Tables.AccessRequests,
                $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.AccessRequests(leagueId))}'"),
            "divisions" => await DeleteByFilterAsync(Constants.Tables.Divisions,
                $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.Divisions(leagueId))}' or PartitionKey eq '{ApiGuards.EscapeOData($"DIVTEMPLATE|{leagueId}")}'"),
            "events" => await DeleteByFilterAsync(Constants.Tables.Events,
                $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.Events(leagueId))}'"),
            "fields" => await DeleteByFilterAsync(Constants.Tables.Fields, PrefixFilter($"FIELD|{leagueId}|")),
            "invites" => await DeleteByFilterAsync(Constants.Tables.LeagueInvites,
                $"PartitionKey eq '{ApiGuards.EscapeOData($"LEAGUEINVITE|{leagueId}")}'"),
            "memberships" => await DeleteByFilterAsync(Constants.Tables.Memberships,
                $"RowKey eq '{ApiGuards.EscapeOData(leagueId)}'"),
            "slotrequests" => await DeleteByFilterAsync(Constants.Tables.SlotRequests, PrefixFilter($"SLOTREQ|{leagueId}|")),
            "slots" => await DeleteByFilterAsync(Constants.Tables.Slots, PrefixFilter($"SLOT|{leagueId}|")),
            "teams" => await DeleteByFilterAsync(Constants.Tables.Teams, PrefixFilter($"TEAM|{leagueId}|")),
            _ => throw new InvalidOperationException($"Unsupported table key: {key}")
        };
    }

    private static string PrefixFilter(string prefix)
        => $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(prefix + "~")}'";

    private async Task<int> DeleteByFilterAsync(string tableName, string filter)
    {
        var table = await TableClients.GetTableAsync(_svc, tableName);
        int deleted = 0;

        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All);
            deleted++;
        }

        return deleted;
    }
}
