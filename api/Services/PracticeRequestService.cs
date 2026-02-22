using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Practice request workflow service.
/// </summary>
public class PracticeRequestService : IPracticeRequestService
{
    private static readonly string[] ActiveRequestStatuses = { "Pending", "Approved" };
    private static readonly string[] PendingStatusOnly = { "Pending" };

    private readonly IPracticeRequestRepository _practiceRequestRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly ILogger<PracticeRequestService> _logger;

    public PracticeRequestService(
        IPracticeRequestRepository practiceRequestRepo,
        IMembershipRepository membershipRepo,
        ISlotRepository slotRepo,
        ITeamRepository teamRepo,
        ILogger<PracticeRequestService> logger)
    {
        _practiceRequestRepo = practiceRequestRepo;
        _membershipRepo = membershipRepo;
        _slotRepo = slotRepo;
        _teamRepo = teamRepo;
        _logger = logger;
    }

    public async Task<TableEntity> CreateRequestAsync(
        string leagueId,
        string userId,
        string division,
        string teamId,
        string slotId,
        string? reason,
        bool openToShareField,
        string? shareWithTeamId)
    {
        division = (division ?? "").Trim();
        teamId = (teamId ?? "").Trim();
        slotId = (slotId ?? "").Trim();
        reason = (reason ?? "").Trim();
        shareWithTeamId = (shareWithTeamId ?? "").Trim();

        if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(slotId))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "division, teamId, and slotId are required");
        }

        ApiGuards.EnsureValidTableKeyPart("division", division);
        ApiGuards.EnsureValidTableKeyPart("teamId", teamId);
        ApiGuards.EnsureValidTableKeyPart("slotId", slotId);
        if (!string.IsNullOrWhiteSpace(shareWithTeamId))
        {
            ApiGuards.EnsureValidTableKeyPart("shareWithTeamId", shareWithTeamId);
        }

        if (!openToShareField)
        {
            shareWithTeamId = "";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(shareWithTeamId))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                    "shareWithTeamId is required when openToShareField is true.");
            }
            if (string.Equals(shareWithTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                    "shareWithTeamId must be another team in the same division.");
            }
        }

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
        var isGlobalAdmin = await _membershipRepo.IsGlobalAdminAsync(userId);
        var isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
        var isCoach = string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase);

        if (!isCoach && !isLeagueAdmin && !isGlobalAdmin)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                "Only coaches can request practice slots");
        }

        if (isCoach && !isLeagueAdmin && !isGlobalAdmin)
        {
            var coachDivision = ReadMembershipDivision(membership);
            var coachTeamId = ReadMembershipTeamId(membership);
            if (string.IsNullOrWhiteSpace(coachDivision) || string.IsNullOrWhiteSpace(coachTeamId))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.COACH_TEAM_REQUIRED,
                    "Coach profile is missing team/division assignment.");
            }

            if (!string.Equals(coachDivision, division, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(coachTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.UNAUTHORIZED,
                    "Coaches can only request practice slots for their assigned team and division.");
            }
        }

        var team = await _teamRepo.GetTeamAsync(leagueId, division, teamId);
        if (team is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.TEAM_NOT_FOUND,
                "Team not found in this division.");
        }

        if (openToShareField)
        {
            var shareTeam = await _teamRepo.GetTeamAsync(leagueId, division, shareWithTeamId!);
            if (shareTeam is null)
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.TEAM_NOT_FOUND,
                    "Proposed shared team was not found in this division.");
            }
        }

        var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
        if (slot is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.SLOT_NOT_FOUND, "Slot not found");
        }

        var slotStatus = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
        if (!string.Equals(slotStatus, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.SLOT_NOT_OPEN,
                "Slot is not available (status must be Open)");
        }

        if (!IsPracticeRequestableSlot(slot))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.SLOT_NOT_AVAILABLE,
                "Only open availability practice slots can be requested.");
        }

        var activeRequestCount = await _practiceRequestRepo.CountRequestsForTeamAsync(
            leagueId, division, teamId, ActiveRequestStatuses);
        if (activeRequestCount >= 3)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "Team already has 3 pending/approved practice requests. Maximum is 3 slots per team.");
        }

        var hasDuplicate = await _practiceRequestRepo.ExistsRequestForTeamSlotAsync(
            leagueId, division, teamId, slotId, ActiveRequestStatuses);
        if (hasDuplicate)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.ALREADY_EXISTS,
                "Team already requested this practice slot");
        }

        var hasActiveSlotRequest = (await _practiceRequestRepo.QuerySlotRequestsAsync(
            leagueId, division, slotId, ActiveRequestStatuses)).Count > 0;
        if (hasActiveSlotRequest)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                "Slot already has an active practice request.");
        }

        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.NewGuid().ToString();
        var requestEntity = new TableEntity(PracticeRequestPk(leagueId), requestId)
        {
            ["LeagueId"] = leagueId,
            ["Division"] = division,
            ["TeamId"] = teamId,
            ["SlotId"] = slotId,
            ["Status"] = "Pending",
            ["Reason"] = reason,
            ["OpenToShareField"] = openToShareField,
            ["ShareWithTeamId"] = shareWithTeamId,
            ["RequestedUtc"] = now,
            ["RequestedBy"] = userId,
            ["UpdatedUtc"] = now
        };

        await _practiceRequestRepo.CreateRequestAsync(requestEntity);

        try
        {
            slot["Status"] = "Pending";
            slot["PendingRequestId"] = requestId;
            slot["PendingTeamId"] = teamId;
            slot["UpdatedUtc"] = now;
            await _slotRepo.UpdateSlotAsync(slot, slot.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            _logger.LogWarning(ex, "CreateRequestAsync conflict reserving slot {LeagueId}/{Division}/{SlotId}", leagueId, division, slotId);
            requestEntity["Status"] = "Rejected";
            requestEntity["ReviewReason"] = "Slot became unavailable during request.";
            requestEntity["ReviewedUtc"] = now;
            requestEntity["ReviewedBy"] = "system";
            requestEntity["UpdatedUtc"] = now;
            try { await _practiceRequestRepo.UpdateRequestAsync(requestEntity, ETag.All); } catch { }

            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                "Slot was requested by another team.");
        }

        return requestEntity;
    }

    public async Task<List<TableEntity>> QueryRequestsAsync(
        string leagueId,
        string userId,
        string? statusFilter,
        string? teamIdFilter)
    {
        statusFilter = (statusFilter ?? "").Trim();
        teamIdFilter = (teamIdFilter ?? "").Trim();

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
        var isGlobalAdmin = await _membershipRepo.IsGlobalAdminAsync(userId);
        var isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
        var isCoach = string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase);

        if (!isCoach && !isLeagueAdmin && !isGlobalAdmin)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                "Only coaches and admins can view practice requests");
        }

        if (isCoach && !isLeagueAdmin && !isGlobalAdmin)
        {
            teamIdFilter = ReadMembershipTeamId(membership);
            if (string.IsNullOrWhiteSpace(teamIdFilter))
            {
                return new List<TableEntity>();
            }
        }

        string? divisionFilter = null;
        if (isCoach && !isLeagueAdmin && !isGlobalAdmin)
        {
            divisionFilter = ReadMembershipDivision(membership);
        }

        var list = await _practiceRequestRepo.QueryRequestsAsync(
            leagueId,
            string.IsNullOrWhiteSpace(statusFilter) ? null : statusFilter,
            divisionFilter,
            string.IsNullOrWhiteSpace(teamIdFilter) ? null : teamIdFilter,
            null);

        return list
            .OrderByDescending(e => e.GetDateTimeOffset("RequestedUtc") ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public async Task<TableEntity> ApproveRequestAsync(
        string leagueId,
        string userId,
        string requestId,
        string? reason)
    {
        reason = (reason ?? "").Trim();
        var membership = await RequireAdminMembershipAsync(leagueId, userId);
        _ = membership;

        var request = await _practiceRequestRepo.GetRequestAsync(leagueId, requestId);
        if (request is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.REQUEST_NOT_FOUND, "Request not found");
        }

        var requestStatus = (request.GetString("Status") ?? "Pending").Trim();
        if (!string.Equals(requestStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.REQUEST_NOT_PENDING,
                $"Request not pending (status: {requestStatus})");
        }

        var division = (request.GetString("Division") ?? "").Trim();
        var teamId = (request.GetString("TeamId") ?? "").Trim();
        var slotId = (request.GetString("SlotId") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(slotId))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_INPUT,
                "Request is missing division, teamId, or slotId.");
        }

        var now = DateTimeOffset.UtcNow;

        // Approve request first; if slot update fails, we roll back to Pending best-effort.
        var approvedRequest = await UpdateRequestStatusAsync(
            leagueId: leagueId,
            requestId: requestId,
            expectedCurrentStatus: "Pending",
            nextStatus: "Approved",
            reviewedBy: userId,
            reviewReason: reason,
            nowUtc: now);

        try
        {
            await RetryUtil.WithEtagRetryAsync(async () =>
            {
                var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
                if (slot is null)
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND, "Slot not found");
                }

                var slotStatus = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
                if (string.Equals(slotStatus, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.SLOT_CANCELLED, "Slot is cancelled.");
                }

                if (string.Equals(slotStatus, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
                {
                    var confirmedRequestId = (slot.GetString("ConfirmedRequestId") ?? "").Trim();
                    if (!string.Equals(confirmedRequestId, requestId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                            "Slot has already been confirmed for another request.");
                    }
                }

                slot["Status"] = Constants.Status.SlotConfirmed;
                slot["ConfirmedRequestId"] = requestId;
                slot["ConfirmedTeamId"] = teamId;
                slot["ConfirmedBy"] = userId;
                slot["ConfirmedUtc"] = now;
                slot["PendingRequestId"] = "";
                slot["PendingTeamId"] = "";
                slot["UpdatedUtc"] = now;
                await _slotRepo.UpdateSlotAsync(slot, slot.ETag);
            });
        }
        catch
        {
            try
            {
                approvedRequest["Status"] = "Pending";
                approvedRequest["ReviewedUtc"] = null;
                approvedRequest["ReviewedBy"] = "";
                approvedRequest["ReviewReason"] = "";
                approvedRequest["UpdatedUtc"] = DateTimeOffset.UtcNow;
                await _practiceRequestRepo.UpdateRequestAsync(approvedRequest, ETag.All);
            }
            catch { }

            throw;
        }

        // Legacy safety: reject any other pending requests for the same slot.
        var others = await _practiceRequestRepo.QuerySlotRequestsAsync(leagueId, division, slotId, PendingStatusOnly);
        foreach (var other in others)
        {
            if (string.Equals(other.RowKey, requestId, StringComparison.OrdinalIgnoreCase))
                continue;

            other["Status"] = "Rejected";
            other["ReviewedUtc"] = now;
            other["ReviewedBy"] = userId;
            other["ReviewReason"] = "Another request for this slot was approved.";
            other["UpdatedUtc"] = now;

            try { await _practiceRequestRepo.UpdateRequestAsync(other, other.ETag); }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex, "Failed to reject competing practice request {RequestId}", other.RowKey);
            }
        }

        return approvedRequest;
    }

    public async Task<TableEntity> RejectRequestAsync(
        string leagueId,
        string userId,
        string requestId,
        string? reason)
    {
        reason = (reason ?? "").Trim();
        var membership = await RequireAdminMembershipAsync(leagueId, userId);
        _ = membership;

        var request = await _practiceRequestRepo.GetRequestAsync(leagueId, requestId);
        if (request is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.REQUEST_NOT_FOUND, "Request not found");
        }

        var requestStatus = (request.GetString("Status") ?? "Pending").Trim();
        if (!string.Equals(requestStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.REQUEST_NOT_PENDING,
                $"Request not pending (status: {requestStatus})");
        }

        var division = (request.GetString("Division") ?? "").Trim();
        var slotId = (request.GetString("SlotId") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(slotId))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_INPUT,
                "Request is missing division or slotId.");
        }

        var now = DateTimeOffset.UtcNow;
        var rejectedRequest = await UpdateRequestStatusAsync(
            leagueId: leagueId,
            requestId: requestId,
            expectedCurrentStatus: "Pending",
            nextStatus: "Rejected",
            reviewedBy: userId,
            reviewReason: reason,
            nowUtc: now);

        // Release reservation: if another pending request exists, move reservation to it; otherwise re-open slot.
        var pendingForSlot = await _practiceRequestRepo.QuerySlotRequestsAsync(leagueId, division, slotId, PendingStatusOnly);
        var nextPending = pendingForSlot
            .OrderBy(e => e.GetDateTimeOffset("RequestedUtc") ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        await RetryUtil.WithEtagRetryAsync(async () =>
        {
            var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
            if (slot is null)
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND, "Slot not found");
            }

            var pendingRequestId = (slot.GetString("PendingRequestId") ?? "").Trim();
            var confirmedRequestId = (slot.GetString("ConfirmedRequestId") ?? "").Trim();
            var slotStatus = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();

            // Only release reservation when this request currently owns the pending reservation
            // or when slot is still pending with no owner set (legacy rows).
            var shouldUpdate =
                string.Equals(slotStatus, "Pending", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(pendingRequestId, requestId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(pendingRequestId));

            if (shouldUpdate)
            {
                if (nextPending is not null)
                {
                    slot["Status"] = "Pending";
                    slot["PendingRequestId"] = nextPending.RowKey;
                    slot["PendingTeamId"] = (nextPending.GetString("TeamId") ?? "").Trim();
                }
                else
                {
                    slot["Status"] = Constants.Status.SlotOpen;
                    slot["PendingRequestId"] = "";
                    slot["PendingTeamId"] = "";
                    if (!string.IsNullOrWhiteSpace(confirmedRequestId))
                    {
                        slot["ConfirmedRequestId"] = "";
                        slot["ConfirmedTeamId"] = "";
                        slot["ConfirmedBy"] = "";
                        slot["ConfirmedUtc"] = null;
                    }
                }
                slot["UpdatedUtc"] = now;
                await _slotRepo.UpdateSlotAsync(slot, slot.ETag);
            }
        });

        return rejectedRequest;
    }

    private async Task<TableEntity?> RequireAdminMembershipAsync(string leagueId, string userId)
    {
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
            return null;

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
        if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                "Only league admins can review practice requests");
        }

        return membership;
    }

    private async Task<TableEntity> UpdateRequestStatusAsync(
        string leagueId,
        string requestId,
        string expectedCurrentStatus,
        string nextStatus,
        string reviewedBy,
        string reviewReason,
        DateTimeOffset nowUtc)
    {
        TableEntity? updated = null;

        await RetryUtil.WithEtagRetryAsync(async () =>
        {
            var fresh = await _practiceRequestRepo.GetRequestAsync(leagueId, requestId);
            if (fresh is null)
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.REQUEST_NOT_FOUND, "Request not found");
            }

            var current = (fresh.GetString("Status") ?? "").Trim();
            if (!string.Equals(current, expectedCurrentStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.REQUEST_NOT_PENDING,
                    $"Request not pending (status: {current})");
            }

            fresh["Status"] = nextStatus;
            fresh["ReviewedUtc"] = nowUtc;
            fresh["ReviewedBy"] = reviewedBy;
            fresh["ReviewReason"] = reviewReason;
            fresh["UpdatedUtc"] = nowUtc;
            await _practiceRequestRepo.UpdateRequestAsync(fresh, fresh.ETag);
            updated = fresh;
        });

        return updated!;
    }

    private static bool IsPracticeRequestableSlot(TableEntity slot)
    {
        return SlotEntityUtil.IsPracticeRequestableAvailability(slot);
    }

    private static string ReadMembershipDivision(TableEntity? membership)
    {
        return (membership?.GetString("Division")
            ?? membership?.GetString("CoachDivision")
            ?? "").Trim();
    }

    private static string ReadMembershipTeamId(TableEntity? membership)
    {
        return (membership?.GetString("TeamId")
            ?? membership?.GetString("CoachTeamId")
            ?? "").Trim();
    }

    private static string PracticeRequestPk(string leagueId) => $"PRACTICEREQ|{leagueId}";
}
