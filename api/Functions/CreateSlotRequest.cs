using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class CreateSlotRequest
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public CreateSlotRequest(ILoggerFactory lf, TableServiceClient svc)
    {
        _log = lf.CreateLogger<CreateSlotRequest>();
        _svc = svc;
    }

    public record CreateReq(string? notes);

    // POST /slots/{division}/{slotId}/requests
    // Accepting a slot immediately confirms it (no approval step).
    [Function("CreateSlotRequest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "slots/{division}/{slotId}/requests")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);

            // Normalize route params FIRST
            var divisionNorm = (division ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(divisionNorm))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Division is required.");
            ApiGuards.EnsureValidTableKeyPart("division", divisionNorm);

            var slotIdNorm = (slotId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(slotIdNorm))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "slotId is required.");
            ApiGuards.EnsureValidTableKeyPart("slotId", slotIdNorm);

            var me = IdentityUtil.GetMe(req);

            // Must be member and not Viewer (global admin bypass)
            await ApiGuards.RequireNotViewerAsync(_svc, me.UserId, leagueId);

            // Coach must have assigned team/division to accept
            var mem = await ApiGuards.GetMembershipAsync(_svc, me.UserId, leagueId);
            var (myDivisionRaw, myTeamIdRaw) = ApiGuards.GetCoachTeam(mem);

            var myDivision = (myDivisionRaw ?? "").Trim().ToUpperInvariant();
            var myTeamId = (myTeamIdRaw ?? "").Trim();

            if (string.IsNullOrWhiteSpace(myTeamId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "COACH_TEAM_REQUIRED",
                    "Coach role requires an assigned team to accept a game request.");

            // Exact division match
            if (!string.IsNullOrWhiteSpace(myDivision) &&
                !string.Equals(myDivision, divisionNorm, StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "DIVISION_MISMATCH",
                    "You can only accept game requests in your exact division.");
            }

            // Tables (CreateIfNotExists to avoid first-run 500s)
            var slots = _svc.GetTableClient(Constants.Tables.Slots);
            var requests = _svc.GetTableClient(Constants.Tables.SlotRequests);
            await slots.CreateIfNotExistsAsync();
            await requests.CreateIfNotExistsAsync();

            // Load slot
            var slotPk = Constants.Pk.Slots(leagueId, divisionNorm);

            TableEntity slot;
            try
            {
                slot = (await slots.GetEntityAsync<TableEntity>(slotPk, slotIdNorm)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Slot not found.");
            }

            var slotStatus = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (!string.Equals(slotStatus, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "SLOT_NOT_OPEN",
                    $"Slot is not open (status: {slotStatus}).");
            }

            // Prevent accepting your own slot
            var offeringTeamId = (slot.GetString("OfferingTeamId") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(offeringTeamId) &&
                string.Equals(offeringTeamId, myTeamId, StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "SELF_ACCEPT_NOT_ALLOWED",
                    "You cannot accept your own game request.");
            }

            // Validate slot time fields
            var gameDate = (slot.GetString("GameDate") ?? "").Trim();
            var startTime = (slot.GetString("StartTime") ?? "").Trim();
            var endTime = (slot.GetString("EndTime") ?? "").Trim();

            if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Slot has invalid GameDate.");

            if (!TimeUtil.IsValidRange(startTime, endTime, out var startMin, out var endMin, out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Slot has invalid StartTime/EndTime.");

            // Double-booking prevention
            var conflicts = new List<object>();

            if (!string.IsNullOrWhiteSpace(offeringTeamId))
            {
                var c = await FindTeamConflictAsync(slots, leagueId, offeringTeamId, gameDate, startMin, endMin, excludeSlotId: slotIdNorm);
                if (c is not null) conflicts.Add(c);
            }

            var myConflict = await FindTeamConflictAsync(slots, leagueId, myTeamId, gameDate, startMin, endMin, excludeSlotId: slotIdNorm);
            if (myConflict is not null) conflicts.Add(myConflict);

            if (conflicts.Count > 0)
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "DOUBLE_BOOKING",
                    "This game overlaps an existing confirmed game for one of the teams.", new { conflicts });
            }

            // Optional body { notes }
            var body = await HttpUtil.ReadJsonAsync<CreateReq>(req);
            var notes = (body?.notes ?? "").Trim();

            var now = DateTimeOffset.UtcNow;
            var requestId = Guid.NewGuid().ToString("N");
            var pk = Constants.Pk.SlotRequests(leagueId, divisionNorm, slotIdNorm);

            // Create request (approved immediately)
            var reqEntity = new TableEntity(pk, requestId)
            {
                ["LeagueId"] = leagueId,
                ["Division"] = divisionNorm,
                ["SlotId"] = slotIdNorm,
                ["RequestId"] = requestId,
                ["RequestingUserId"] = me.UserId,
                ["RequestingTeamId"] = myTeamId,
                ["RequestingEmail"] = me.Email ?? "",
                ["Notes"] = notes,
                ["Status"] = Constants.Status.SlotRequestApproved,
                ["ApprovedBy"] = me.Email ?? "",
                ["ApprovedUtc"] = now,
                ["RequestedUtc"] = now,
                ["UpdatedUtc"] = now
            };

            await requests.AddEntityAsync(reqEntity);

            // Immediately confirm the slot for the requesting team
            try
            {
                slot["Status"] = Constants.Status.SlotConfirmed;
                slot["ConfirmedTeamId"] = myTeamId;
                slot["ConfirmedRequestId"] = requestId;
                slot["ConfirmedBy"] = me.Email ?? "";
                slot["ConfirmedUtc"] = now;
                slot["UpdatedUtc"] = now;

                await slots.UpdateEntityAsync(slot, slot.ETag, TableUpdateMode.Replace);
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                // Best-effort: mark request denied
                reqEntity["Status"] = Constants.Status.SlotRequestDenied;
                reqEntity["RejectedUtc"] = now;
                reqEntity["UpdatedUtc"] = now;
                try { await requests.UpdateEntityAsync(reqEntity, ETag.All, TableUpdateMode.Replace); } catch { }

                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "Slot was confirmed by another team.");
            }

            // Best-effort: reject other pending requests for this slot
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
            await foreach (var other in requests.QueryAsync<TableEntity>(filter: filter))
            {
                if (other.RowKey == requestId) continue;

                var st = (other.GetString("Status") ?? Constants.Status.SlotRequestPending).Trim();
                if (!string.Equals(st, Constants.Status.SlotRequestPending, StringComparison.OrdinalIgnoreCase))
                    continue;

                other["Status"] = Constants.Status.SlotRequestDenied;
                other["RejectedUtc"] = now;
                other["UpdatedUtc"] = now;

                try { await requests.UpdateEntityAsync(other, other.ETag, TableUpdateMode.Replace); } catch { }
            }

            return ApiResponses.Ok(req, new
            {
                requestId,
                requestingTeamId = myTeamId,
                status = Constants.Status.SlotRequestApproved,
                slotStatus = Constants.Status.SlotConfirmed,
                confirmedTeamId = myTeamId,
                requestedUtc = now
            }, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateSlotRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static async Task<object?> FindTeamConflictAsync(
        TableClient slots,
        string leagueId,
        string teamId,
        string gameDate,
        int startMin,
        int endMin,
        string? excludeSlotId)
    {
        // Scan confirmed slots in this league (all divisions) for same date.
        // PK prefix: SLOT|{leagueId}|{division}
        var pkPrefix = $"SLOT|{leagueId}|";
        var next = pkPrefix + "\uffff";

        var filter =
            $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}' " +
            $"and GameDate eq '{ApiGuards.EscapeOData(gameDate)}' and Status eq '{ApiGuards.EscapeOData(Constants.Status.SlotConfirmed)}'";

        await foreach (var e in slots.QueryAsync<TableEntity>(filter: filter))
        {
            var conflictSlotId = e.RowKey;
            if (!string.IsNullOrWhiteSpace(excludeSlotId) &&
                string.Equals(conflictSlotId, excludeSlotId, StringComparison.OrdinalIgnoreCase))
                continue;

            var offeringTeamId = (e.GetString("OfferingTeamId") ?? "").Trim();
            var confirmedTeamId = (e.GetString("ConfirmedTeamId") ?? "").Trim();

            var involvesTeam =
                string.Equals(offeringTeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(confirmedTeamId, teamId, StringComparison.OrdinalIgnoreCase);

            if (!involvesTeam) continue;

            var st = (e.GetString("StartTime") ?? "").Trim();
            var et = (e.GetString("EndTime") ?? "").Trim();

            if (!TimeUtil.IsValidRange(st, et, out var s2, out var e2, out _)) continue;
            if (!TimeUtil.Overlaps(startMin, endMin, s2, e2)) continue;

            return new
            {
                teamId,
                conflict = new
                {
                    slotId = conflictSlotId,
                    division = ExtractDivision(e.PartitionKey, leagueId),
                    gameDate,
                    startTime = st,
                    endTime = et,
                    offeringTeamId,
                    confirmedTeamId
                }
            };
        }

        return null;
    }

    private static string ExtractDivision(string pk, string leagueId)
    {
        var prefix = $"SLOT|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }
}
