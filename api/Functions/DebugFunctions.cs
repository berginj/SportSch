using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class DebugFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public DebugFunctions(ILoggerFactory lf, TableServiceClient svc)
    {
        _svc = svc;
        _log = lf.CreateLogger<DebugFunctions>();
    }

    [Function("DebugLeagueTables")]
    public async Task<HttpResponseData> LeagueTables(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/debug/league/{leagueId}")] HttpRequestData req,
        string leagueId)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me.UserId);

            leagueId = (leagueId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(leagueId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "leagueId is required");

            var output = new Dictionary<string, object?>
            {
                ["leagueId"] = leagueId,
                ["leagues"] = await ReadByPkAsync(Constants.Tables.Leagues, Constants.Pk.Leagues, leagueId),
                ["memberships"] = await ReadByFilterAsync(Constants.Tables.Memberships,
                    $"RowKey eq '{ApiGuards.EscapeOData(leagueId)}'"),
                ["accessRequests"] = await ReadByFilterAsync(Constants.Tables.AccessRequests,
                    $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.AccessRequests(leagueId))}'"),
                ["divisions"] = await ReadByFilterAsync(Constants.Tables.Divisions,
                    $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.Divisions(leagueId))}' or PartitionKey eq '{ApiGuards.EscapeOData($"DIVTEMPLATE|{leagueId}")}'"),
                ["events"] = await ReadByFilterAsync(Constants.Tables.Events,
                    $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.Events(leagueId))}'"),
                ["fields"] = await ReadByFilterAsync(Constants.Tables.Fields, PrefixFilter($"FIELD|{leagueId}|")),
                ["invites"] = await ReadByFilterAsync(Constants.Tables.LeagueInvites,
                    $"PartitionKey eq '{ApiGuards.EscapeOData($"LEAGUEINVITE|{leagueId}")}'"),
                ["slotRequests"] = await ReadByFilterAsync(Constants.Tables.SlotRequests, PrefixFilter($"SLOTREQ|{leagueId}|")),
                ["slots"] = await ReadByFilterAsync(Constants.Tables.Slots, PrefixFilter($"SLOT|{leagueId}|")),
                ["teams"] = await ReadByFilterAsync(Constants.Tables.Teams, PrefixFilter($"TEAM|{leagueId}|")),
                ["scheduleRuns"] = await ReadByFilterAsync(Constants.Tables.ScheduleRuns, PrefixFilter($"SCHED|{leagueId}|")),
                ["fieldAvailabilityRules"] = await ReadByFilterAsync(Constants.Tables.FieldAvailabilityRules,
                    PrefixFilter($"AVAILRULE|{leagueId}|"))
            };

            return ApiResponses.Ok(req, output);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "DebugLeagueTables failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private async Task<List<Dictionary<string, object?>>> ReadByPkAsync(string tableName, string pk, string rk)
    {
        var table = await TableClients.GetTableAsync(_svc, tableName);
        var list = new List<Dictionary<string, object?>>();
        try
        {
            var entity = (await table.GetEntityAsync<TableEntity>(pk, rk)).Value;
            list.Add(ToPlain(entity));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // empty
        }
        return list;
    }

    private async Task<List<Dictionary<string, object?>>> ReadByFilterAsync(string tableName, string filter)
    {
        var table = await TableClients.GetTableAsync(_svc, tableName);
        var list = new List<Dictionary<string, object?>>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
            list.Add(ToPlain(entity));
        return list;
    }

    private static string PrefixFilter(string prefix)
        => $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(prefix + "~")}'";

    private static Dictionary<string, object?> ToPlain(TableEntity entity)
    {
        var map = new Dictionary<string, object?>
        {
            ["PartitionKey"] = entity.PartitionKey,
            ["RowKey"] = entity.RowKey,
            ["Timestamp"] = entity.Timestamp?.ToString("O")
        };

        foreach (var kvp in entity)
        {
            if (kvp.Key is "PartitionKey" or "RowKey" or "Timestamp" or "etag")
                continue;

            map[kvp.Key] = kvp.Value switch
            {
                DateTimeOffset dto => dto.ToString("O"),
                JsonElement json => json.ToString(),
                _ => kvp.Value
            };
        }

        return map;
    }
}
