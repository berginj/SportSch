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
        string city,
        string state,
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
                    city: e.GetString("City") ?? "",
                    state: e.GetString("State") ?? "",
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

    public record UpdateFieldRequest(
        string? displayName,
        string? address,
        string? city,
        string? state,
        string? notes
    );

    [Function("UpdateField")]
    public async Task<HttpResponseData> UpdateField(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "fields/{parkCode}/{fieldCode}")]
        HttpRequestData req,
        string parkCode,
        string fieldCode)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            UpdateFieldRequest? body;
            try { body = await req.ReadFromJsonAsync<UpdateFieldRequest>(); }
            catch { return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body"); }

            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            parkCode = (parkCode ?? "").Trim();
            fieldCode = (fieldCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "parkCode and fieldCode are required");
            ApiGuards.EnsureValidTableKeyPart("parkCode", parkCode);
            ApiGuards.EnsureValidTableKeyPart("fieldCode", fieldCode);

            var pk = Constants.Pk.Fields(leagueId, parkCode);
            var rk = fieldCode;

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
            TableEntity entity;
            try
            {
                entity = (await table.GetEntityAsync<TableEntity>(pk, rk)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Field not found");
            }

            if (body.displayName is not null) entity["DisplayName"] = body.displayName;
            if (body.address is not null) entity["Address"] = body.address;
            if (body.city is not null) entity["City"] = body.city;
            if (body.state is not null) entity["State"] = body.state;
            if (body.notes is not null) entity["Notes"] = body.notes;
            entity["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge);

            return ApiResponses.Ok(req, new
            {
                fieldKey = $"{parkCode}/{fieldCode}",
                displayName = entity.GetString("DisplayName") ?? "",
                address = entity.GetString("Address") ?? "",
                city = entity.GetString("City") ?? "",
                state = entity.GetString("State") ?? "",
                notes = entity.GetString("Notes") ?? ""
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateField failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
