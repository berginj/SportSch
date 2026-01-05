using System.Globalization;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class ClaimPracticeSlot
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public ClaimPracticeSlot(ILoggerFactory lf, TableServiceClient svc)
    {
        _log = lf.CreateLogger<ClaimPracticeSlot>();
        _svc = svc;
    }

    public record ClaimPracticeReq(string? notes, string? teamId);

    [Function("ClaimPracticeSlot")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "slots/{division}/{slotId}/practice")] HttpRequestData req,
        string division,
        string slotId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);

            var divisionNorm = (division ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(divisionNorm))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Division is required.");
            ApiGuards.EnsureValidTableKeyPart("division", divisionNorm);

            var slotIdNorm = (slotId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(slotIdNorm))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "slotId is required.");
            ApiGuards.EnsureValidTableKeyPart("slotId", slotIdNorm);

            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireNotViewerAsync(_svc, me.UserId, leagueId);

            var mem = await ApiGuards.GetMembershipAsync(_svc, me.UserId, leagueId);
            var role = ApiGuards.GetRole(mem);
            var isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
            var isGlobalAdmin = await ApiGuards.IsGlobalAdminAsync(_svc, me.UserId);

            var (myDivisionRaw, myTeamIdRaw) = ApiGuards.GetCoachTeam(mem);
            var myDivision = (myDivisionRaw ?? "").Trim().ToUpperInvariant();
            var myTeamId = (myTeamIdRaw ?? "").Trim();

            var body = await HttpUtil.ReadJsonAsync<ClaimPracticeReq>(req);
            var notes = (body?.notes ?? "").Trim();
            var overrideTeamId = (body?.teamId ?? "").Trim();

            var canOverrideTeam = isGlobalAdmin || isLeagueAdmin;
            if (string.IsNullOrWhiteSpace(myTeamId))
            {
                if (!canOverrideTeam)
                {
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "COACH_TEAM_REQUIRED",
                        "Coach role requires an assigned team to select a practice slot.");
                }

                if (string.IsNullOrWhiteSpace(overrideTeamId))
                {
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "TEAM_REQUIRED",
                        "Select a team to claim this practice slot.");
                }
                ApiGuards.EnsureValidTableKeyPart("teamId", overrideTeamId);

                var teams = await TableClients.GetTableAsync(_svc, Constants.Tables.Teams);
                try
                {
                    _ = (await teams.GetEntityAsync<TableEntity>(Constants.Pk.Teams(leagueId, divisionNorm), overrideTeamId)).Value;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "NOT_FOUND",
                        "Team not found in this division.");
                }

                myTeamId = overrideTeamId;
                myDivision = divisionNorm;
            }

            if (!string.IsNullOrWhiteSpace(myDivision) &&
                !string.Equals(myDivision, divisionNorm, StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "DIVISION_MISMATCH",
                    "You can only select practice slots in your exact division.");
            }

            var slots = _svc.GetTableClient(Constants.Tables.Slots);
            var requests = _svc.GetTableClient(Constants.Tables.SlotRequests);
            await slots.CreateIfNotExistsAsync();
            await requests.CreateIfNotExistsAsync();

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

            var isAvailability = slot.GetBoolean("IsAvailability") ?? false;
            if (!isAvailability)
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "SLOT_NOT_AVAILABLE",
                    "Only unassigned availability slots can be claimed for practice.");
            }

            var gameDate = (slot.GetString("GameDate") ?? "").Trim();
            var startTime = (slot.GetString("StartTime") ?? "").Trim();
            var endTime = (slot.GetString("EndTime") ?? "").Trim();

            if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", out var gameDay))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Slot has invalid GameDate.");

            if (!TimeUtil.IsValidRange(startTime, endTime, out var startMin, out var endMin, out _))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Slot has invalid StartTime/EndTime.");

            var conflicts = new List<object>();
            var myConflict = await FindTeamConflictAsync(slots, leagueId, myTeamId, gameDate, startMin, endMin, excludeSlotId: slotIdNorm);
            if (myConflict is not null) conflicts.Add(myConflict);

            if (conflicts.Count > 0)
            {
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "DOUBLE_BOOKING",
                    "This practice overlaps an existing confirmed slot for your team.", new { conflicts });
            }

            var weekKey = WeekKey(gameDay);
            if (!string.IsNullOrWhiteSpace(weekKey))
            {
                var practiceConflict = await FindPracticeWeekConflictAsync(slots, leagueId, myTeamId, weekKey);
                if (practiceConflict is not null)
                {
                    return ApiResponses.Error(req, HttpStatusCode.Conflict, "PRACTICE_LIMIT",
                        "You already selected a practice slot for this week.", practiceConflict);
                }
            }

            var now = DateTimeOffset.UtcNow;
            var requestId = Guid.NewGuid().ToString("N");
            var pk = Constants.Pk.SlotRequests(leagueId, divisionNorm, slotIdNorm);

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

            slot["Status"] = Constants.Status.SlotConfirmed;
            slot["ConfirmedTeamId"] = myTeamId;
            slot["ConfirmedRequestId"] = requestId;
            slot["ConfirmedBy"] = me.Email ?? "";
            slot["ConfirmedUtc"] = now;
            slot["OfferingTeamId"] = myTeamId;
            slot["OfferingEmail"] = me.Email ?? "";
            slot["IsAvailability"] = false;
            slot["GameType"] = "Practice";
            slot["UpdatedUtc"] = now;

            await slots.UpdateEntityAsync(slot, slot.ETag, TableUpdateMode.Replace);

            return ApiResponses.Ok(req, new
            {
                slotId = slotIdNorm,
                division = divisionNorm,
                status = Constants.Status.SlotConfirmed,
                confirmedTeamId = myTeamId,
                gameType = "Practice",
                weekKey,
                requestedUtc = now
            }, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ClaimPracticeSlot failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }

    private static async Task<object?> FindPracticeWeekConflictAsync(
        TableClient slots,
        string leagueId,
        string teamId,
        string weekKey)
    {
        var pkPrefix = $"SLOT|{leagueId}|";
        var next = pkPrefix + "\uffff";

        var filter =
            $"PartitionKey ge '{ApiGuards.EscapeOData(pkPrefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}' " +
            $"and Status eq '{ApiGuards.EscapeOData(Constants.Status.SlotConfirmed)}' and GameType eq 'Practice'";

        await foreach (var e in slots.QueryAsync<TableEntity>(filter: filter))
        {
            var confirmedTeamId = (e.GetString("ConfirmedTeamId") ?? "").Trim();
            var offeringTeamId = (e.GetString("OfferingTeamId") ?? "").Trim();
            if (!string.Equals(confirmedTeamId, teamId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(offeringTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            var gameDate = (e.GetString("GameDate") ?? "").Trim();
            if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", out var date))
                continue;

            var otherKey = WeekKey(date);
            if (!string.Equals(otherKey, weekKey, StringComparison.OrdinalIgnoreCase))
                continue;

            return new
            {
                teamId,
                week = weekKey,
                slotId = e.RowKey,
                division = ExtractDivision(e.PartitionKey, leagueId),
                gameDate
            };
        }

        return null;
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

    private static string WeekKey(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{dt.Year}-W{week:D2}";
    }

    private static string ExtractDivision(string pk, string leagueId)
    {
        var prefix = $"SLOT|{leagueId}|";
        if (!pk.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return pk[prefix.Length..];
    }
}
