using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

public class ApproveSlotRequest
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string SlotsTableName = Constants.Tables.Slots;
    private const string RequestsTableName = Constants.Tables.SlotRequests;

    public ApproveSlotRequest(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<ApproveSlotRequest>();
        _svc = tableServiceClient;
    }

    public record ApproveReq(string? approvedByEmail);

    [Function("ApproveSlotRequest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "slots/{division}/{slotId}/requests/{requestId}/approve")] HttpRequestData req,
        string division,
        string slotId,
        string requestId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireNotViewerAsync(_svc, me.UserId, leagueId);

            division = (division ?? "").Trim();
            slotId = (slotId ?? "").Trim();
            requestId = (requestId ?? "").Trim();
            ApiGuards.EnsureValidTableKeyPart("division", division);
            ApiGuards.EnsureValidTableKeyPart("slotId", slotId);
            ApiGuards.EnsureValidTableKeyPart("requestId", requestId);

            // Authorization: offering coach OR LeagueAdmin OR global admin.
            var isGlobalAdmin = await ApiGuards.IsGlobalAdminAsync(_svc, me.UserId);
            var mem = isGlobalAdmin ? null : await ApiGuards.GetMembershipAsync(_svc, me.UserId, leagueId);
            var role = isGlobalAdmin ? Constants.Roles.LeagueAdmin : ApiGuards.GetRole(mem);

            var body = await HttpUtil.ReadJsonAsync<ApproveReq>(req);
            var approvedBy = (body?.approvedByEmail ?? me.Email ?? "").Trim();

            var slots = await TableClients.GetTableAsync(_svc, SlotsTableName);
            var requests = await TableClients.GetTableAsync(_svc, RequestsTableName);
            var slotPk = $"SLOT|{leagueId}|{division}";
            TableEntity slot;
            try { slot = (await slots.GetEntityAsync<TableEntity>(slotPk, slotId)).Value; }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Slot not found");
            }

            var offeringTeamId = (slot.GetString("OfferingTeamId") ?? "").Trim();
            var isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
            if (!isGlobalAdmin && !isLeagueAdmin)
            {
                // Coach can only approve requests for their own offered slots.
                if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, "FORBIDDEN", "Forbidden");

                var (myDivision, myTeamId) = ApiGuards.GetCoachTeam(mem);
                if (string.IsNullOrWhiteSpace(myTeamId))
                    return ApiResponses.Error(req, HttpStatusCode.BadRequest, "COACH_TEAM_REQUIRED", "Coach must be assigned to a team to approve slot requests.");
                if (!string.IsNullOrWhiteSpace(myDivision) && !string.Equals(myDivision.Trim(), (division ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, "FORBIDDEN", "Forbidden");
                if (!string.Equals(myTeamId.Trim(), offeringTeamId, StringComparison.OrdinalIgnoreCase))
                    return ApiResponses.Error(req, HttpStatusCode.Forbidden, "FORBIDDEN", "Only the offering coach (or LeagueAdmin) can approve this slot request.");
            }

            var status = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "Slot is cancelled");

            // With immediate-confirmation semantics, this endpoint is effectively idempotent.
            if (string.Equals(status, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
            {
                var confirmedReqId = (slot.GetString("ConfirmedRequestId") ?? "").Trim();
                if (string.Equals(confirmedReqId, requestId, StringComparison.OrdinalIgnoreCase))
                    return ApiResponses.Ok(req, new { ok = true, slotId, division, requestId, status = Constants.Status.SlotConfirmed });
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", "Slot already confirmed");
            }

            var reqPk = $"SLOTREQ|{leagueId}|{division}|{slotId}";
            TableEntity request;
            try { request = (await requests.GetEntityAsync<TableEntity>(reqPk, requestId)).Value; }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Request not found");
            }

            var reqStatus = (request.GetString("Status") ?? Constants.Status.SlotRequestPending).Trim();
            if (!string.Equals(reqStatus, Constants.Status.SlotRequestPending, StringComparison.OrdinalIgnoreCase))
                return ApiResponses.Error(req, HttpStatusCode.Conflict, "CONFLICT", $"Request not pending (status: {reqStatus})");

            var now = DateTimeOffset.UtcNow;

            // Approve this request
            request["Status"] = Constants.Status.SlotRequestApproved;
            request["ApprovedBy"] = approvedBy;
            request["ApprovedUtc"] = now;
            request["UpdatedUtc"] = now;
            await requests.UpdateEntityAsync(request, request.ETag, TableUpdateMode.Replace);

            // Reject all other pending requests for the slot
            var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(reqPk)}'";
            await foreach (var other in requests.QueryAsync<TableEntity>(filter: filter))
            {
                if (other.RowKey == requestId) continue;
                var otherStatus = (other.GetString("Status") ?? Constants.Status.SlotRequestPending).Trim();
                if (!string.Equals(otherStatus, Constants.Status.SlotRequestPending, StringComparison.OrdinalIgnoreCase)) continue;

                other["Status"] = Constants.Status.SlotRequestDenied;
                other["RejectedUtc"] = now;
                other["UpdatedUtc"] = now;

                try { await requests.UpdateEntityAsync(other, other.ETag, TableUpdateMode.Replace); }
                catch (RequestFailedException ex)
                {
                    _log.LogWarning(ex, "Failed to reject request {requestId} for slot {slotId}", other.RowKey, slotId);
                }
            }

            // Confirm slot
            slot["Status"] = Constants.Status.SlotConfirmed;
            slot["ConfirmedTeamId"] = request.GetString("RequestingTeamId") ?? "";
            slot["ConfirmedRequestId"] = requestId;
            slot["ConfirmedBy"] = approvedBy;
            slot["ConfirmedUtc"] = now;
            slot["UpdatedUtc"] = now;

            await slots.UpdateEntityAsync(slot, slot.ETag, TableUpdateMode.Replace);

            UsageTelemetry.Track(_log, "api_slot_request_approve", leagueId, me.UserId, new
            {
                division,
                slotId,
                requestId
            });

            return ApiResponses.Ok(req, new { ok = true, slotId, division, requestId, status = Constants.Status.SlotConfirmed });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "ApproveSlotRequest failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
