using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using System.Linq;

namespace GameSwap.Functions.Functions;

public class FieldsFunctions
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public FieldsFunctions(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<FieldsFunctions>();
        _svc = tableServiceClient;
    }

    public record FieldDto(
        string fieldKey,
        string parkName,
        string fieldName,
        string displayName,
        string address,
        string notes,
        string status
    );

    [Function("ListFields")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fields")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var activeOnly = GetBoolQuery(req, "activeOnly", defaultValue: true);

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
            // PK convention: FIELD|{leagueId}|{parkCode}, RK = fieldCode
            var pkPrefix = $"FIELD|{leagueId}|";
            var next = pkPrefix + "\uffff";
            var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

            var list = new List<FieldDto>();

            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var isActive = e.GetBoolean("IsActive") ?? true;
                if (activeOnly && !isActive) continue;

                var parkCode = ExtractParkCodeFromPk(e.PartitionKey, leagueId);
                var fieldCode = e.RowKey;

                var parkName = e.GetString("ParkName") ?? "";
                var fieldName = e.GetString("FieldName") ?? "";
                var displayName = e.GetString("DisplayName") ?? $"{parkName} > {fieldName}";

                list.Add(new FieldDto(
                    fieldKey: $"{parkCode}/{fieldCode}",
                    parkName: parkName,
                    fieldName: fieldName,
                    displayName: displayName,
                    address: e.GetString("Address") ?? "",
                    notes: e.GetString("Notes") ?? "",
                    status: isActive ? Constants.Status.FieldActive : Constants.Status.FieldInactive
                ));
            }

            return ApiResponses.Ok(req, list.OrderBy(x => x.displayName));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListFields failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static string ExtractParkCodeFromPk(string pk, string leagueId)
    {
        var prefix = $"FIELD|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }

    private static bool GetBoolQuery(HttpRequestData req, string key, bool defaultValue)
    {
        var v = ApiGuards.GetQueryParam(req, key);
        if (string.IsNullOrWhiteSpace(v)) return defaultValue;
        return bool.TryParse(v, out var b) ? b : defaultValue;
    }
}
