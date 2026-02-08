using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class SeasonReset
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public SeasonReset(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<SeasonReset>();
        _svc = tableServiceClient;
    }

    public record SeasonResetRequest(string? confirm);

    [Function("SeasonResetLeagueData")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/season-reset")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<SeasonResetRequest>(req);
            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");
            if (!string.Equals((body.confirm ?? "").Trim(), "RESET SEASON", StringComparison.OrdinalIgnoreCase))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Missing confirm=RESET SEASON");

            var errors = new List<object>();
            var slotRequestsDeleted = await RunDeleteStepAsync(
                "slotRequests",
                () => DeleteByFilterAsync(Constants.Tables.SlotRequests, PrefixFilter($"SLOTREQ|{leagueId}|")),
                errors);
            var slotsDeleted = await RunDeleteStepAsync(
                "slots",
                () => DeleteByFilterAsync(Constants.Tables.Slots, PrefixFilter($"SLOT|{leagueId}|")),
                errors);
            var eventsDeleted = await RunDeleteStepAsync(
                "events",
                () => DeleteByFilterAsync(Constants.Tables.Events, $"PartitionKey eq '{ApiGuards.EscapeOData(Constants.Pk.Events(leagueId))}'"),
                errors);
            var allocationsDeleted = await RunDeleteStepAsync(
                "availabilityAllocations",
                () => DeleteByFilterAsync(Constants.Tables.FieldAvailabilityAllocations, PrefixFilter($"ALLOC|{leagueId}|")),
                errors);

            HashSet<string> ruleIds = new(StringComparer.OrdinalIgnoreCase);
            var rulesDeleted = await RunDeleteStepAsync(
                "availabilityRules",
                async () =>
                {
                    var result = await DeleteAvailabilityRulesAndCollectRuleIdsAsync(leagueId);
                    ruleIds = result.ruleIds;
                    return result.deleted;
                },
                errors);

            var exceptionsDeleted = await RunDeleteStepAsync(
                "availabilityExceptions",
                () => DeleteAvailabilityExceptionsByRuleIdsAsync(ruleIds),
                errors);

            var fieldsDeleted = await RunDeleteStepAsync(
                "fields",
                () => DeleteByFilterAsync(Constants.Tables.Fields, PrefixFilter($"FIELD|{leagueId}|")),
                errors);
            var scheduleRunsDeleted = await RunDeleteStepAsync(
                "scheduleRuns",
                () => DeleteByFilterAsync(Constants.Tables.ScheduleRuns, PrefixFilter($"SCHED|{leagueId}|")),
                errors);

            var totalDeleted = slotRequestsDeleted + slotsDeleted + eventsDeleted + allocationsDeleted + rulesDeleted
                + exceptionsDeleted + fieldsDeleted + scheduleRunsDeleted;

            return ApiResponses.Ok(req, new
            {
                leagueId,
                deleted = new
                {
                    slotRequests = slotRequestsDeleted,
                    slots = slotsDeleted,
                    events = eventsDeleted,
                    availabilityAllocations = allocationsDeleted,
                    availabilityRules = rulesDeleted,
                    availabilityExceptions = exceptionsDeleted,
                    fields = fieldsDeleted,
                    scheduleRuns = scheduleRunsDeleted,
                    total = totalDeleted
                },
                errors
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            var requestId = req.FunctionContext.InvocationId.ToString();
            _log.LogError(ex, "SeasonReset failed. requestId={requestId}", requestId);
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                "INTERNAL",
                "Season reset failed.",
                new { requestId, exception = ex.GetType().Name, message = ex.Message });
        }
    }

    private async Task<int> RunDeleteStepAsync(string category, Func<Task<int>> action, List<object> errors)
    {
        try
        {
            return await action();
        }
        catch (RequestFailedException ex)
        {
            _log.LogError(ex, "Season reset delete step failed: {category}", category);
            errors.Add(new { category, error = ex.Message, status = ex.Status, code = ex.ErrorCode });
            return 0;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Season reset delete step failed: {category}", category);
            errors.Add(new { category, error = ex.Message });
            return 0;
        }
    }

    private async Task<(int deleted, HashSet<string> ruleIds)> DeleteAvailabilityRulesAndCollectRuleIdsAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityRules);
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<(string pk, string rk)>();

        await foreach (var e in table.QueryAsync<TableEntity>(filter: PrefixFilter($"AVAILRULE|{leagueId}|")))
        {
            var ruleId = (e.RowKey ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(ruleId)) ruleIds.Add(ruleId);
            entities.Add((e.PartitionKey, e.RowKey ?? ""));
        }

        await foreach (var e in table.QueryAsync<TableEntity>(filter: PrefixFilter($"AVAILRULEIDX|{leagueId}|")))
        {
            entities.Add((e.PartitionKey, e.RowKey ?? ""));
        }

        var deleted = 0;
        foreach (var e in entities)
        {
            await table.DeleteEntityAsync(e.pk, e.rk, ETag.All);
            deleted++;
        }

        return (deleted, ruleIds);
    }

    private async Task<int> DeleteAvailabilityExceptionsByRuleIdsAsync(IEnumerable<string> ruleIds)
    {
        var table = await TableClients.GetTableAsync(_svc, Constants.Tables.FieldAvailabilityExceptions);
        var deleted = 0;

        foreach (var ruleId in ruleIds)
        {
            var pk = Constants.Pk.FieldAvailabilityRuleExceptions(ruleId);
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All);
                deleted++;
            }
        }

        return deleted;
    }

    private static string PrefixFilter(string prefix)
    {
        var next = prefix + "\uffff";
        return $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
    }

    private async Task<int> DeleteByFilterAsync(string tableName, string filter)
    {
        var table = await TableClients.GetTableAsync(_svc, tableName);
        var deleted = 0;
        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All);
            deleted++;
        }
        return deleted;
    }
}
