using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Functions;

public class AdminMigrateFields
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public AdminMigrateFields(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<AdminMigrateFields>();
        _svc = tableServiceClient;
    }

    [Function("AdminMigrateFields")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/migrate/fields")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
            var oldPrefix = $"FIELD#{leagueId}#";
            var next = oldPrefix + "\uffff";
            var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(oldPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

            int migrated = 0;
            var errors = new List<object>();

            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                try
                {
                    var parkCode = ExtractParkCodeFromOldPk(e.PartitionKey, leagueId);
                    if (string.IsNullOrWhiteSpace(parkCode))
                        parkCode = (e.GetString("ParkCode") ?? "").Trim();

                    var newPk = Constants.Pk.Fields(leagueId, parkCode);
                    var fieldCode = e.RowKey;

                    var newEntity = new TableEntity(newPk, fieldCode);
                    foreach (var kvp in e)
                    {
                        if (string.Equals(kvp.Key, "PartitionKey", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(kvp.Key, "RowKey", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(kvp.Key, "Timestamp", StringComparison.OrdinalIgnoreCase)) continue;
                        newEntity[kvp.Key] = kvp.Value;
                    }

                    if (!newEntity.ContainsKey("ParkCode") && !string.IsNullOrWhiteSpace(parkCode))
                        newEntity["ParkCode"] = parkCode;
                    if (!newEntity.ContainsKey("FieldCode"))
                        newEntity["FieldCode"] = fieldCode;
                    if (!newEntity.ContainsKey("FieldKey") && !string.IsNullOrWhiteSpace(parkCode))
                        newEntity["FieldKey"] = $"{parkCode}/{fieldCode}";

                    newEntity["UpdatedUtc"] = DateTimeOffset.UtcNow;

                    await table.UpsertEntityAsync(newEntity, TableUpdateMode.Merge);
                    migrated++;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Field migration failed for pk={pk} rk={rk}", e.PartitionKey, e.RowKey);
                    errors.Add(new { pk = e.PartitionKey, rk = e.RowKey, error = ex.Message });
                }
            }

            return ApiResponses.Ok(req, new { leagueId, migrated, errors });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "AdminMigrateFields failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static string ExtractParkCodeFromOldPk(string pk, string leagueId)
    {
        var prefix = $"FIELD#{leagueId}#";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }
}
