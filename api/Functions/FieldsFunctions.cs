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

    public record BlackoutRange(string? startDate, string? endDate, string? label);
    public record FieldDto(
        string fieldKey,
        string parkName,
        string fieldName,
        string displayName,
        string address,
        string city,
        string state,
        string notes,
        string status,
        List<BlackoutRange> blackouts
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
                    status: isActive ? Constants.Status.FieldActive : Constants.Status.FieldInactive,
                    blackouts: ParseBlackouts(e.GetString("Blackouts"))
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
        string? parkName,
        string? fieldName,
        string? displayName,
        string? address,
        string? city,
        string? state,
        string? notes,
        string? status,
        List<BlackoutRange>? blackouts
    );

    public record CreateFieldRequest(
        string? fieldKey,
        string? parkName,
        string? fieldName,
        string? displayName,
        string? address,
        string? city,
        string? state,
        string? notes,
        string? status,
        List<BlackoutRange>? blackouts
    );

    [Function("CreateField")]
    public async Task<HttpResponseData> CreateField(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "fields")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            CreateFieldRequest? body;
            try { body = await req.ReadFromJsonAsync<CreateFieldRequest>(); }
            catch { return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body"); }

            if (body is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var fieldKey = (body.fieldKey ?? "").Trim();
            var parkName = (body.parkName ?? "").Trim();
            var fieldName = (body.fieldName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fieldKey))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey is required");
            if (string.IsNullOrWhiteSpace(parkName))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "parkName is required");
            if (string.IsNullOrWhiteSpace(fieldName))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldName is required");

            var parts = fieldKey.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey must be parkCode/fieldCode");

            var parkCode = parts[0].Trim();
            var fieldCode = parts[1].Trim();
            ApiGuards.EnsureValidTableKeyPart("parkCode", parkCode);
            ApiGuards.EnsureValidTableKeyPart("fieldCode", fieldCode);

            var status = NormalizeFieldStatus(body.status);
            if (status is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "status must be Active or Inactive");

            var displayName = (body.displayName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = $"{parkName} > {fieldName}";

            var pk = Constants.Pk.Fields(leagueId, parkCode);
            var rk = fieldCode;
            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);

            var now = DateTimeOffset.UtcNow;
            var entity = new TableEntity(pk, rk)
            {
                ["ParkName"] = parkName,
                ["FieldName"] = fieldName,
                ["DisplayName"] = displayName,
                ["Address"] = (body.address ?? "").Trim(),
                ["City"] = (body.city ?? "").Trim(),
                ["State"] = (body.state ?? "").Trim(),
                ["Notes"] = (body.notes ?? "").Trim(),
                ["IsActive"] = status == Constants.Status.FieldActive,
                ["Blackouts"] = body.blackouts is null
                    ? "[]"
                    : System.Text.Json.JsonSerializer.Serialize(body.blackouts),
                ["UpdatedUtc"] = now
            };

            await table.UpsertEntityAsync(entity, TableUpdateMode.Merge);

            return ApiResponses.Ok(req, new FieldDto(
                fieldKey: $"{parkCode}/{fieldCode}",
                parkName: parkName,
                fieldName: fieldName,
                displayName: displayName,
                address: entity.GetString("Address") ?? "",
                city: entity.GetString("City") ?? "",
                state: entity.GetString("State") ?? "",
                notes: entity.GetString("Notes") ?? "",
                status: status,
                blackouts: ParseBlackouts(entity.GetString("Blackouts"))
            ));
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateField failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

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

            var status = NormalizeFieldStatus(body.status);
            if (body.status is not null && status is null)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "status must be Active or Inactive");

            if (body.displayName is not null) entity["DisplayName"] = body.displayName;
            if (body.parkName is not null) entity["ParkName"] = body.parkName;
            if (body.fieldName is not null) entity["FieldName"] = body.fieldName;
            if (body.address is not null) entity["Address"] = body.address;
            if (body.city is not null) entity["City"] = body.city;
            if (body.state is not null) entity["State"] = body.state;
            if (body.notes is not null) entity["Notes"] = body.notes;
            if (status is not null) entity["IsActive"] = status == Constants.Status.FieldActive;
            if (body.blackouts is not null)
                entity["Blackouts"] = System.Text.Json.JsonSerializer.Serialize(body.blackouts);
            entity["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge);

            return ApiResponses.Ok(req, new
            {
                fieldKey = $"{parkCode}/{fieldCode}",
                displayName = entity.GetString("DisplayName") ?? "",
                address = entity.GetString("Address") ?? "",
                city = entity.GetString("City") ?? "",
                state = entity.GetString("State") ?? "",
                notes = entity.GetString("Notes") ?? "",
                status = (entity.GetBoolean("IsActive") ?? true) ? Constants.Status.FieldActive : Constants.Status.FieldInactive,
                blackouts = ParseBlackouts(entity.GetString("Blackouts"))
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateField failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    [Function("DeleteField")]
    public async Task<HttpResponseData> DeleteField(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "fields/{parkCode}/{fieldCode}")]
        HttpRequestData req,
        string parkCode,
        string fieldCode)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            parkCode = (parkCode ?? "").Trim();
            fieldCode = (fieldCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "parkCode and fieldCode are required");
            ApiGuards.EnsureValidTableKeyPart("parkCode", parkCode);
            ApiGuards.EnsureValidTableKeyPart("fieldCode", fieldCode);

            var pk = Constants.Pk.Fields(leagueId, parkCode);
            var rk = fieldCode;

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);
            try
            {
                await table.DeleteEntityAsync(pk, rk);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Field not found");
            }

            return ApiResponses.Ok(req, new { fieldKey = $"{parkCode}/{fieldCode}" });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeleteField failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static List<BlackoutRange> ParseBlackouts(string? raw)
    {
        var blackoutsRaw = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(blackoutsRaw)) return new List<BlackoutRange>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<BlackoutRange>>(blackoutsRaw) ?? new List<BlackoutRange>();
        }
        catch
        {
            return new List<BlackoutRange>();
        }
    }

    private static string? NormalizeFieldStatus(string? status)
    {
        if (status is null) return null;
        if (string.IsNullOrWhiteSpace(status)) return null;
        if (status.Equals(Constants.Status.FieldActive, StringComparison.OrdinalIgnoreCase))
            return Constants.Status.FieldActive;
        if (status.Equals(Constants.Status.FieldInactive, StringComparison.OrdinalIgnoreCase))
            return Constants.Status.FieldInactive;
        return null;
    }
}
