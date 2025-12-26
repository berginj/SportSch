using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Functions;

public class CreateSlot
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string SlotsTableName = Constants.Tables.Slots;
    private const string FieldsTableName = Constants.Tables.Fields;

    public CreateSlot(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<CreateSlot>();
        _svc = tableServiceClient;
    }

    public record CreateSlotReq(
        string? division,
        string? offeringTeamId,
        string? gameDate,
        string? startTime,
        string? endTime,
        string? fieldKey,
        string? parkName,
        string? fieldName,
        string? offeringEmail,
        string? gameType,
        string? notes
    );

    [Function("CreateSlot")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "slots")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireNotViewerAsync(_svc, me.UserId, leagueId);

            var body = await HttpUtil.ReadJsonAsync<CreateSlotReq>(req);
            if (body is null) return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var division = (body.division ?? "").Trim();
            var offeringTeamId = (body.offeringTeamId ?? "").Trim();
            var offeringEmail = (body.offeringEmail ?? me.Email ?? "").Trim();

            var gameDate = (body.gameDate ?? "").Trim();
            var startTime = (body.startTime ?? "").Trim();
            var endTime = (body.endTime ?? "").Trim();

            var fieldKey = (body.fieldKey ?? "").Trim();
            var parkName = (body.parkName ?? "").Trim();   // optional (back-compat)
            var fieldName = (body.fieldName ?? "").Trim(); // optional (back-compat)

            var gameType = string.IsNullOrWhiteSpace(body.gameType) ? "Swap" : body.gameType!.Trim();
            var notes = (body.notes ?? "").Trim();

            if (string.IsNullOrWhiteSpace(division) ||
                string.IsNullOrWhiteSpace(offeringTeamId) ||
                string.IsNullOrWhiteSpace(gameDate) ||
                string.IsNullOrWhiteSpace(startTime) ||
                string.IsNullOrWhiteSpace(endTime) ||
                string.IsNullOrWhiteSpace(fieldKey))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST",
                    "division, offeringTeamId, gameDate, startTime, endTime, fieldKey are required");
            }
            ApiGuards.EnsureValidTableKeyPart("division", division);

            // Validate date/time formats (times are interpreted as US/Eastern per contract; stored as strings)
            if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "gameDate must be YYYY-MM-DD.");

            if (!TimeUtil.IsValidRange(startTime, endTime, out _, out _, out var timeErr))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", timeErr);

            // Enforce Coach restrictions: coach can only create slots for their assigned team/division.
            var isGlobalAdmin = await ApiGuards.IsGlobalAdminAsync(_svc, me.UserId);
            if (!isGlobalAdmin)
            {
                var mem = await ApiGuards.GetMembershipAsync(_svc, me.UserId, leagueId);
                var role = ApiGuards.GetRole(mem);

                if (string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
                {
                    var (myDivision, myTeamId) = ApiGuards.GetCoachTeam(mem);
                    if (string.IsNullOrWhiteSpace(myTeamId))
                        return ApiResponses.Error(req, HttpStatusCode.BadRequest, "COACH_TEAM_REQUIRED", "Coach role requires an assigned team to offer a slot.");

                    if (!string.Equals(myDivision, division, StringComparison.OrdinalIgnoreCase))
                        return ApiResponses.Error(req, HttpStatusCode.Conflict, "DIVISION_MISMATCH", "You can only offer slots within your assigned division (exact match).");

                    if (!string.Equals(myTeamId, offeringTeamId, StringComparison.OrdinalIgnoreCase))
                        return ApiResponses.Error(req, HttpStatusCode.Forbidden, "FORBIDDEN", "You can only offer slots for your assigned team.");
                }
            }

            // Validate field exists + active.
            var fieldsTable = await TableClients.GetTableAsync(_svc, FieldsTableName);
            if (!TryParseFieldKey(fieldKey, out var parkCode, out var fieldCode))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "fieldKey must be parkCode/fieldCode.");

            var fieldPk = Constants.Pk.Fields(leagueId, parkCode);
            var fieldRk = fieldCode;

            TableEntity field;
            try { field = (await fieldsTable.GetEntityAsync<TableEntity>(fieldPk, fieldRk)).Value; }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Field not found. Import fields first.");
            }

            var isActive = field.GetBoolean("IsActive") ?? true;
            if (!isActive)
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "Field exists but is inactive.");

            var normalizedParkName = field.GetString("ParkName") ?? parkName;
            var normalizedFieldName = field.GetString("FieldName") ?? fieldName;
            var displayName = field.GetString("DisplayName") ?? $"{normalizedParkName} > {normalizedFieldName}";

            var slotsTable = await TableClients.GetTableAsync(_svc, SlotsTableName);
            var slotId = Guid.NewGuid().ToString("N");
            var pk = Constants.Pk.Slots(leagueId, division);
            var now = DateTimeOffset.UtcNow;

            var entity = new TableEntity(pk, slotId)
            {
                ["LeagueId"] = leagueId,
                ["SlotId"] = slotId,
                ["Division"] = division,

                ["OfferingTeamId"] = offeringTeamId,
                ["HomeTeamId"] = offeringTeamId,
                ["AwayTeamId"] = "",
                ["IsExternalOffer"] = false,
                ["IsAvailability"] = false,
                ["OfferingEmail"] = offeringEmail,

                ["GameDate"] = gameDate,
                ["StartTime"] = startTime,
                ["EndTime"] = endTime,

                ["ParkName"] = normalizedParkName,
                ["FieldName"] = normalizedFieldName,
                ["DisplayName"] = displayName,
                ["FieldKey"] = $"{parkCode}/{fieldCode}",

                ["GameType"] = gameType,
                ["Status"] = Constants.Status.SlotOpen,
                ["Notes"] = notes,

                ["CreatedUtc"] = now,
                ["UpdatedUtc"] = now
            };

            await slotsTable.AddEntityAsync(entity);

            return ApiResponses.Ok(req, new
            {
                division,
                slotId = entity.RowKey,
                offeringTeamId,
                gameDate,
                startTime,
                endTime,
                parkName = entity.GetString("ParkName") ?? "",
                fieldName = entity.GetString("FieldName") ?? "",
                displayName = entity.GetString("DisplayName") ?? "",
                fieldKey = entity.GetString("FieldKey") ?? "",
                gameType,
                status = (entity.GetString("Status") ?? Constants.Status.SlotOpen).Trim(),
                notes
            }, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateSlot failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static bool TryParseFieldKey(string raw, out string parkCode, out string fieldCode)
    {
        parkCode = "";
        fieldCode = "";
        var v = (raw ?? "").Trim().Trim('/');
        var parts = v.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        parkCode = Slug.Make(parts[0]);
        fieldCode = Slug.Make(parts[1]);
        return !string.IsNullOrWhiteSpace(parkCode) && !string.IsNullOrWhiteSpace(fieldCode);
    }
}
