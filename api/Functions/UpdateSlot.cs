using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Updates slot scheduling fields (date/time/field) with conflict checks.
/// </summary>
public class UpdateSlot
{
    private readonly ISlotRepository _slotRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger _log;

    public UpdateSlot(
        ISlotRepository slotRepo,
        IFieldRepository fieldRepo,
        IMembershipRepository membershipRepo,
        ILoggerFactory lf)
    {
        _slotRepo = slotRepo;
        _fieldRepo = fieldRepo;
        _membershipRepo = membershipRepo;
        _log = lf.CreateLogger<UpdateSlot>();
    }

    public record UpdateSlotReq(
        string? gameDate,
        string? startTime,
        string? endTime,
        string? fieldKey
    );

    [Function("UpdateSlot")]
    [OpenApiOperation(operationId: "UpdateSlot", tags: new[] { "Slots" }, Summary = "Update slot schedule details", Description = "Updates game date/time/field for an existing slot with conflict checks. LeagueAdmin or GlobalAdmin only.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Division code")]
    [OpenApiParameter(name: "slotId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Slot identifier")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateSlotReq), Required = true, Description = "Updated schedule fields")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Slot updated")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Conflict, contentType: "application/json", bodyType: typeof(object), Description = "Field/time conflict detected")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins can edit scheduled games")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "slots/{division}/{slotId}")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            division = (division ?? "").Trim();
            slotId = (slotId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("division", division);
            ApiGuards.EnsureValidTableKeyPart("slotId", slotId);

            if (!await IsLeagueAdminAsync(me.UserId, leagueId))
            {
                return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN, "Only league admins can edit scheduled games.");
            }

            var body = await HttpUtil.ReadJsonAsync<UpdateSlotReq>(req);
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
            if (slot is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND, "Slot not found.");
            }

            var isAvailability = slot.GetBoolean("IsAvailability") ?? false;
            if (isAvailability)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Availability slots cannot be edited from this action.");
            }

            var currentStatus = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (string.Equals(currentStatus, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, ErrorCodes.INVALID_STATUS_TRANSITION, "Cancelled slots cannot be edited.");
            }

            var targetGameDate = NormalizeOrFallback(body.gameDate, slot.GetString("GameDate"));
            var targetStartTime = NormalizeOrFallback(body.startTime, slot.GetString("StartTime"));
            var targetEndTime = NormalizeOrFallback(body.endTime, slot.GetString("EndTime"));
            var targetFieldKeyRaw = NormalizeOrFallback(body.fieldKey, slot.GetString("FieldKey"));

            if (string.IsNullOrWhiteSpace(targetGameDate) ||
                string.IsNullOrWhiteSpace(targetStartTime) ||
                string.IsNullOrWhiteSpace(targetEndTime) ||
                string.IsNullOrWhiteSpace(targetFieldKeyRaw))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.MISSING_REQUIRED_FIELD, "gameDate, startTime, endTime, and fieldKey are required.");
            }

            if (!DateTimeUtil.IsValidDate(targetGameDate))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_DATE, "gameDate must be in YYYY-MM-DD format.");
            }

            if (!TimeUtil.IsValidRange(targetStartTime, targetEndTime, out var startMin, out var endMin, out var timeErr))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_TIME_RANGE, timeErr);
            }

            if (!FieldKeyUtil.TryParseFieldKey(targetFieldKeyRaw, out var parkCode, out var fieldCode))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.INVALID_FIELD_KEY, "fieldKey must be in format parkCode/fieldCode");
            }

            var normalizedFieldKey = FieldKeyUtil.NormalizeFieldKey(parkCode, fieldCode);
            var field = await _fieldRepo.GetFieldAsync(leagueId, parkCode, fieldCode);
            if (field is null || !(field.GetBoolean("IsActive") ?? true))
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, ErrorCodes.FIELD_NOT_FOUND, $"Field not found or inactive: {normalizedFieldKey}");
            }

            var conflicts = await FindConflictsAsync(leagueId, normalizedFieldKey, targetGameDate, startMin, endMin, slotId);
            if (conflicts.Count > 0)
            {
                return ApiResponses.Error(
                    req,
                    HttpStatusCode.Conflict,
                    ErrorCodes.SLOT_CONFLICT,
                    $"Selected field/time overlaps {conflicts.Count} existing slot(s).",
                    new
                    {
                        conflictCount = conflicts.Count,
                        conflicts
                    });
            }

            slot["GameDate"] = targetGameDate;
            slot["StartTime"] = targetStartTime;
            slot["EndTime"] = targetEndTime;
            slot["StartMin"] = startMin;
            slot["EndMin"] = endMin;
            slot["FieldKey"] = normalizedFieldKey;
            slot["ParkName"] = (field.GetString("ParkName") ?? "").Trim();
            slot["FieldName"] = (field.GetString("FieldName") ?? "").Trim();
            slot["DisplayName"] = (field.GetString("DisplayName") ?? $"{slot.GetString("ParkName")} > {slot.GetString("FieldName")}").Trim();
            slot["UpdatedUtc"] = DateTimeOffset.UtcNow;
            slot["UpdatedBy"] = me.UserId;

            await _slotRepo.UpdateSlotAsync(slot, slot.ETag);

            UsageTelemetry.Track(_log, "api_slot_update", leagueId, me.UserId, new
            {
                division,
                slotId,
                targetGameDate,
                targetStartTime,
                targetEndTime,
                targetFieldKey = normalizedFieldKey
            });

            return ApiResponses.Ok(req, EntityMappers.MapSlot(slot));
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "UpdateSlot failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }

    private async Task<List<object>> FindConflictsAsync(
        string leagueId,
        string fieldKey,
        string gameDate,
        int startMin,
        int endMin,
        string slotIdToExclude)
    {
        var slots = await _slotRepo.GetSlotsByFieldAndDateAsync(leagueId, fieldKey, gameDate);
        var conflicts = new List<object>();
        foreach (var slot in slots)
        {
            if (string.Equals(slot.RowKey, slotIdToExclude, StringComparison.OrdinalIgnoreCase))
                continue;

            var status = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                continue;

            var slotStart = slot.GetInt32("StartMin");
            if (!slotStart.HasValue && TimeUtil.TryParseMinutes(slot.GetString("StartTime") ?? "", out var parsedStart))
                slotStart = parsedStart;

            var slotEnd = slot.GetInt32("EndMin");
            if (!slotEnd.HasValue && TimeUtil.TryParseMinutes(slot.GetString("EndTime") ?? "", out var parsedEnd))
                slotEnd = parsedEnd;

            if (!slotStart.HasValue || !slotEnd.HasValue)
                continue;

            if (!TimeUtil.Overlaps(startMin, endMin, slotStart.Value, slotEnd.Value))
                continue;

            conflicts.Add(new
            {
                slotId = slot.RowKey,
                division = (slot.GetString("Division") ?? "").Trim(),
                gameDate = (slot.GetString("GameDate") ?? "").Trim(),
                startTime = (slot.GetString("StartTime") ?? "").Trim(),
                endTime = (slot.GetString("EndTime") ?? "").Trim(),
                status,
                homeTeamId = (slot.GetString("HomeTeamId") ?? "").Trim(),
                awayTeamId = (slot.GetString("AwayTeamId") ?? "").Trim(),
                isAvailability = slot.GetBoolean("IsAvailability") ?? false
            });
        }

        return conflicts;
    }

    private async Task<bool> IsLeagueAdminAsync(string userId, string leagueId)
    {
        if (await _membershipRepo.IsGlobalAdminAsync(userId)) return true;
        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? "").Trim();
        return string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeOrFallback(string? candidate, string? fallback)
    {
        var normalized = (candidate ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        return (fallback ?? "").Trim();
    }
}
