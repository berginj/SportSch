using System.Net;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Functions for field management.
/// Refactored to use repository layer for data access.
/// </summary>
public class FieldsFunctions
{
    private readonly IFieldRepository _fieldRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger _log;

    public FieldsFunctions(IFieldRepository fieldRepo, IMembershipRepository membershipRepo, ILoggerFactory lf)
    {
        _fieldRepo = fieldRepo;
        _membershipRepo = membershipRepo;
        _log = lf.CreateLogger<FieldsFunctions>();
    }

    public record BlackoutRange(string? startDate, string? endDate, string? label);
    public record FieldDto(
        string fieldKey,
        string parkName,
        string fieldName,
        string division,
        string displayName,
        string address,
        string city,
        string state,
        string notes,
        string status,
        List<BlackoutRange> blackouts
    );

    public record UpdateFieldRequest(
        string? parkName,
        string? fieldName,
        string? division,
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
        string? division,
        string? displayName,
        string? address,
        string? city,
        string? state,
        string? notes,
        string? status,
        List<BlackoutRange>? blackouts
    );

    [Function("ListFields")]
    [OpenApiOperation(operationId: "ListFields", tags: new[] { "Fields" }, Summary = "List fields", Description = "Retrieves all fields for a league with optional park code filter. Returns field details including blackout dates.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "parkCode", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Optional park code to filter fields")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Fields retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a member of this league)")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fields")] HttpRequestData req)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be member
            if (!await _membershipRepo.IsMemberAsync(me.UserId, leagueId) &&
                !await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                    "Access denied: no membership for this league");
            }

            var activeOnly = GetBoolQuery(req, "activeOnly", defaultValue: true);

            // Query fields from repository
            var fields = await _fieldRepo.QueryFieldsAsync(leagueId);

            var list = new List<FieldDto>();
            foreach (var e in fields)
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
                    division: e.GetString("Division") ?? "",
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
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ListFields failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("CreateField")]
    [OpenApiOperation(operationId: "CreateField", tags: new[] { "Fields" }, Summary = "Create field", Description = "Creates a new field for a league. Only league admins can create fields.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreateFieldRequest), Required = true, Description = "Field details (fieldKey, parkName, fieldName, address, etc.)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Field created successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request (missing required fields or invalid fieldKey format)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Conflict, contentType: "application/json", bodyType: typeof(object), Description = "Field already exists")]
    public async Task<HttpResponseData> CreateField(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "fields")] HttpRequestData req)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be league admin
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can create fields");
                }
            }

            // Parse body
            CreateFieldRequest? body;
            try { body = await req.ReadFromJsonAsync<CreateFieldRequest>(); }
            catch { return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body"); }

            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            var fieldKey = (body.fieldKey ?? "").Trim();
            var parkName = (body.parkName ?? "").Trim();
            var fieldName = (body.fieldName ?? "").Trim();

            // Validate required fields
            if (string.IsNullOrWhiteSpace(fieldKey))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "fieldKey is required");
            }
            if (string.IsNullOrWhiteSpace(parkName))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "parkName is required");
            }
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "fieldName is required");
            }

            // Parse field key
            if (!FieldKeyUtil.TryParseFieldKey(fieldKey, out var parkCode, out var fieldCode))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_FIELD_KEY, "fieldKey must be parkCode/fieldCode");
            }

            var status = NormalizeFieldStatus(body.status);
            if (status is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_INPUT, "status must be Active or Inactive");
            }

            var displayName = (body.displayName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = $"{parkName} > {fieldName}";
            }

            // Check for duplicate by name
            var existingFields = await _fieldRepo.QueryFieldsAsync(leagueId);
            var nameKey = NormalizeNameKey(parkName, fieldName);
            foreach (var existing in existingFields)
            {
                var existingParkName = (existing.GetString("ParkName") ?? "").Trim();
                var existingFieldName = (existing.GetString("FieldName") ?? "").Trim();
                var existingKey = NormalizeNameKey(existingParkName, existingFieldName);

                if (string.Equals(existingKey, nameKey, StringComparison.OrdinalIgnoreCase))
                {
                    var existingParkCode = ExtractParkCodeFromPk(existing.PartitionKey, leagueId);
                    var existingFieldCode = existing.RowKey;
                    var existingFieldKey = $"{existingParkCode}/{existingFieldCode}";
                    var normalizedFieldKey = $"{parkCode}/{fieldCode}";

                    if (!string.Equals(existingFieldKey, normalizedFieldKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return ApiResponses.Error(req, HttpStatusCode.Conflict, ErrorCodes.ALREADY_EXISTS,
                            $"Field already exists for {parkName} / {fieldName}. Use fieldKey {existingFieldKey} instead.");
                    }
                }
            }

            // Create field entity
            var now = DateTimeOffset.UtcNow;
            var pk = Constants.Pk.Fields(leagueId, parkCode);
            var entity = new Azure.Data.Tables.TableEntity(pk, fieldCode)
            {
                ["ParkName"] = parkName,
                ["FieldName"] = fieldName,
                ["Division"] = (body.division ?? "").Trim(),
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

            await _fieldRepo.CreateFieldAsync(entity);

            _log.LogInformation("Field created: {FieldKey} for league {LeagueId}", fieldKey, leagueId);

            return ApiResponses.Ok(req, new FieldDto(
                fieldKey: $"{parkCode}/{fieldCode}",
                parkName: parkName,
                fieldName: fieldName,
                division: entity.GetString("Division") ?? "",
                displayName: displayName,
                address: entity.GetString("Address") ?? "",
                city: entity.GetString("City") ?? "",
                state: entity.GetString("State") ?? "",
                notes: entity.GetString("Notes") ?? "",
                status: status,
                blackouts: ParseBlackouts(entity.GetString("Blackouts"))
            ));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateField failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("UpdateField")]
    [OpenApiOperation(operationId: "UpdateField", tags: new[] { "Fields" }, Summary = "Update field", Description = "Updates an existing field's details. Only league admins can update fields.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "parkCode", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Park code")]
    [OpenApiParameter(name: "fieldCode", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Field code")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateFieldRequest), Required = true, Description = "Updated field details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Field updated successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Field not found")]
    public async Task<HttpResponseData> UpdateField(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "fields/{parkCode}/{fieldCode}")]
        HttpRequestData req,
        string parkCode,
        string fieldCode)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be league admin
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can update fields");
                }
            }

            // Parse body
            UpdateFieldRequest? body;
            try { body = await req.ReadFromJsonAsync<UpdateFieldRequest>(); }
            catch { return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body"); }

            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            parkCode = (parkCode ?? "").Trim();
            fieldCode = (fieldCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "parkCode and fieldCode are required");
            }
            ApiGuards.EnsureValidTableKeyPart("parkCode", parkCode);
            ApiGuards.EnsureValidTableKeyPart("fieldCode", fieldCode);

            // Get existing field
            var entity = await _fieldRepo.GetFieldAsync(leagueId, parkCode, fieldCode);
            if (entity == null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, ErrorCodes.FIELD_NOT_FOUND, "Field not found");
            }

            var status = NormalizeFieldStatus(body.status);
            if (body.status is not null && status is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_INPUT, "status must be Active or Inactive");
            }

            // Update fields
            if (body.displayName is not null) entity["DisplayName"] = body.displayName;
            if (body.parkName is not null) entity["ParkName"] = body.parkName;
            if (body.fieldName is not null) entity["FieldName"] = body.fieldName;
            if (body.division is not null) entity["Division"] = body.division.Trim();
            if (body.address is not null) entity["Address"] = body.address;
            if (body.city is not null) entity["City"] = body.city;
            if (body.state is not null) entity["State"] = body.state;
            if (body.notes is not null) entity["Notes"] = body.notes;
            if (status is not null) entity["IsActive"] = status == Constants.Status.FieldActive;
            if (body.blackouts is not null)
            {
                entity["Blackouts"] = System.Text.Json.JsonSerializer.Serialize(body.blackouts);
            }
            entity["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await _fieldRepo.UpdateFieldAsync(entity);

            _log.LogInformation("Field updated: {ParkCode}/{FieldCode} for league {LeagueId}", parkCode, fieldCode, leagueId);

            return ApiResponses.Ok(req, new
            {
                fieldKey = $"{parkCode}/{fieldCode}",
                division = entity.GetString("Division") ?? "",
                displayName = entity.GetString("DisplayName") ?? "",
                address = entity.GetString("Address") ?? "",
                city = entity.GetString("City") ?? "",
                state = entity.GetString("State") ?? "",
                notes = entity.GetString("Notes") ?? "",
                status = (entity.GetBoolean("IsActive") ?? true) ? Constants.Status.FieldActive : Constants.Status.FieldInactive,
                blackouts = ParseBlackouts(entity.GetString("Blackouts"))
            });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateField failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    [Function("DeleteField")]
    [OpenApiOperation(operationId: "DeleteField", tags: new[] { "Fields" }, Summary = "Delete field", Description = "Soft deletes a field by deactivating it. Only league admins can delete fields.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "parkCode", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Park code")]
    [OpenApiParameter(name: "fieldCode", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Field code")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Field deleted successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid request")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Unauthorized (not a league admin)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Field not found")]
    public async Task<HttpResponseData> DeleteField(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "fields/{parkCode}/{fieldCode}")]
        HttpRequestData req,
        string parkCode,
        string fieldCode)
    {
        try
        {
            // Extract context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            // Authorization - must be league admin
            if (!await _membershipRepo.IsGlobalAdminAsync(me.UserId))
            {
                var membership = await _membershipRepo.GetMembershipAsync(me.UserId, leagueId);
                var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
                if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                        "Only league admins can delete fields");
                }
            }

            parkCode = (parkCode ?? "").Trim();
            fieldCode = (fieldCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "parkCode and fieldCode are required");
            }
            ApiGuards.EnsureValidTableKeyPart("parkCode", parkCode);
            ApiGuards.EnsureValidTableKeyPart("fieldCode", fieldCode);

            // Soft delete by deactivating
            await _fieldRepo.DeactivateFieldAsync(leagueId, parkCode, fieldCode);

            _log.LogInformation("Field deleted: {ParkCode}/{FieldCode} for league {LeagueId}", parkCode, fieldCode, leagueId);

            return ApiResponses.Ok(req, new { fieldKey = $"{parkCode}/{fieldCode}" });
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeleteField failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    // ========== Helper Methods ==========

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

    private static string NormalizeNameKey(string parkName, string fieldName)
        => $"{Slug.Make(parkName)}|{Slug.Make(fieldName)}";
}
