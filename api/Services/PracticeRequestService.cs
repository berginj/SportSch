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
        string? shareWithTeamId,
        int? priority = null)
    {
        return await CreateRequestCoreAsync(
            leagueId,
            userId,
            division,
            teamId,
            slotId,
            reason,
            openToShareField,
            shareWithTeamId,
            priority,
            excludedActiveRequestId: null,
            extraProperties: null);
    }

    public async Task<TableEntity> CreateMoveRequestAsync(
        string leagueId,
        string userId,
        string sourceRequestId,
        string targetSlotId,
        string? reason,
        bool openToShareField = false,
        string? shareWithTeamId = null)
    {
        var sourceId = (sourceRequestId ?? "").Trim();
        var targetId = (targetSlotId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "sourceRequestId and targetSlotId are required.");
        }

        var sourceRequest = await _practiceRequestRepo.GetRequestAsync(leagueId, sourceId);
        if (sourceRequest is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.REQUEST_NOT_FOUND, "Request not found");
        }

        await EnsureRequestOwnerOrAdminAsync(leagueId, userId, sourceRequest);

        var sourceStatus = (sourceRequest.GetString("Status") ?? "").Trim();
        if (!string.Equals(sourceStatus, "Approved", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sourceStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.PRACTICE_MOVE_NOT_ALLOWED,
                $"Only active practice requests can be moved (status: {sourceStatus}).");
        }

        var division = (sourceRequest.GetString("Division") ?? "").Trim();
        var teamId = (sourceRequest.GetString("TeamId") ?? "").Trim();
        var sourceSlotId = (sourceRequest.GetString("SlotId") ?? "").Trim();
        if (string.Equals(sourceSlotId, targetId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.PRACTICE_MOVE_NOT_ALLOWED,
                "Choose a different practice slot when moving a request.");
        }

        var extraProperties = new Dictionary<string, object?>
        {
            ["RequestKind"] = "Move",
            ["MoveFromRequestId"] = sourceId,
            ["MoveFromSlotId"] = sourceSlotId,
            ["MoveFromStatus"] = sourceStatus,
        };

        return await CreateRequestCoreAsync(
            leagueId,
            userId,
            division,
            teamId,
            targetId,
            reason,
            openToShareField,
            shareWithTeamId,
            priority: sourceRequest.GetInt32("Priority"),
            excludedActiveRequestId: sourceId,
            extraProperties: extraProperties);
    }

    private async Task<TableEntity> CreateRequestCoreAsync(
        string leagueId,
        string userId,
        string division,
        string teamId,
        string slotId,
        string? reason,
        bool openToShareField,
        string? shareWithTeamId,
        int? priority,
        string? excludedActiveRequestId,
        IReadOnlyDictionary<string, object?>? extraProperties)
    {
        division = (division ?? "").Trim();
        teamId = (teamId ?? "").Trim();
        slotId = (slotId ?? "").Trim();
        reason = (reason ?? "").Trim();
        shareWithTeamId = (shareWithTeamId ?? "").Trim();
        excludedActiveRequestId = (excludedActiveRequestId ?? "").Trim();

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

        var teamRequests = await _practiceRequestRepo.QueryRequestsAsync(leagueId, null, division, teamId, null);
        var activeRequests = (teamRequests ?? Enumerable.Empty<TableEntity>())
            .Where(e =>
                ActiveRequestStatuses.Contains((e.GetString("Status") ?? "").Trim(), StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(e.RowKey, excludedActiveRequestId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (activeRequests.Count >= 3)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "Team already has 3 pending/approved practice requests. Maximum is 3 slots per team.");
        }

        var usedPriorities = new HashSet<int>(
            activeRequests
                .Select(e => e.GetInt32("Priority") ?? 0)
                .Where(p => p is >= 1 and <= 3));

        var resolvedPriority = priority ?? 0;
        if (resolvedPriority <= 0)
        {
            resolvedPriority = Enumerable.Range(1, 3).FirstOrDefault(p => !usedPriorities.Contains(p));
        }

        if (resolvedPriority < 1 || resolvedPriority > 3)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "priority must be 1, 2, or 3 (or omitted to auto-assign).");
        }

        if (usedPriorities.Contains(resolvedPriority))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                $"Priority {resolvedPriority} is already in use for another active practice request.");
        }

        var hasDuplicate = activeRequests.Any(e =>
            string.Equals((e.GetString("SlotId") ?? "").Trim(), slotId, StringComparison.OrdinalIgnoreCase));
        if (hasDuplicate)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.ALREADY_EXISTS,
                "Team already requested this practice slot");
        }

        var activeSlotRequests = await _practiceRequestRepo.QuerySlotRequestsAsync(leagueId, division, slotId, ActiveRequestStatuses);
        var hasActiveSlotRequest = activeSlotRequests.Any(e =>
            !string.Equals(e.RowKey, excludedActiveRequestId, StringComparison.OrdinalIgnoreCase));
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
            ["Priority"] = resolvedPriority,
            ["Status"] = "Pending",
            ["Reason"] = reason,
            ["OpenToShareField"] = openToShareField,
            ["ShareWithTeamId"] = shareWithTeamId,
            ["RequestedUtc"] = now,
            ["RequestedBy"] = userId,
            ["UpdatedUtc"] = now
        };
        CaptureRequestSlotSnapshot(requestEntity, slot);

        if (extraProperties is not null)
        {
            foreach (var (key, value) in extraProperties)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (value is null)
                {
                    requestEntity.Remove(key);
                }
                else
                {
                    requestEntity[key] = value;
                }
            }
        }

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
        await RequireAdminMembershipAsync(leagueId, userId);
        return await ApproveRequestCoreAsync(leagueId, userId, requestId, reason);
    }

    public async Task<TableEntity> AutoApproveRequestAsync(
        string leagueId,
        string requestId,
        string reviewedBy,
        string? reason)
    {
        return await ApproveRequestCoreAsync(leagueId, reviewedBy, requestId, reason);
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

    public async Task<TableEntity> CancelRequestAsync(
        string leagueId,
        string userId,
        string requestId,
        string? reason = null)
    {
        var request = await _practiceRequestRepo.GetRequestAsync(leagueId, (requestId ?? "").Trim());
        if (request is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.REQUEST_NOT_FOUND, "Request not found");
        }

        await EnsureRequestOwnerOrAdminAsync(leagueId, userId, request);
        return await CancelRequestCoreAsync(leagueId, request.RowKey, userId, reason, request);
    }

    private async Task<TableEntity> ApproveRequestCoreAsync(
        string leagueId,
        string reviewedBy,
        string requestId,
        string? reason)
    {
        reason = (reason ?? "").Trim();

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
        var approvedRequest = await UpdateRequestStatusAsync(
            leagueId: leagueId,
            requestId: requestId,
            expectedCurrentStatus: "Pending",
            nextStatus: "Approved",
            reviewedBy: reviewedBy,
            reviewReason: reason,
            nowUtc: now);

        List<string> confirmedSlotIds;
        try
        {
            confirmedSlotIds = await ConfirmPracticeRequestSlotsAsync(
                leagueId: leagueId,
                division: division,
                teamId: teamId,
                requestId: requestId,
                approvedRequest: approvedRequest,
                approvedByUserId: reviewedBy,
                nowUtc: now);
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

        await RejectCompetingPendingSlotRequestsAsync(leagueId, division, requestId, reviewedBy, now, confirmedSlotIds);
        await FinalizeApprovedMoveSourceAsync(leagueId, approvedRequest, reviewedBy, now);

        return approvedRequest;
    }

    private async Task<TableEntity> CancelRequestCoreAsync(
        string leagueId,
        string requestId,
        string reviewedBy,
        string? reason,
        TableEntity? request)
    {
        var reviewReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled" : reason.Trim();
        var now = DateTimeOffset.UtcNow;
        TableEntity? cancelledRequest = null;

        await RetryUtil.WithEtagRetryAsync(async () =>
        {
            var fresh = request ?? await _practiceRequestRepo.GetRequestAsync(leagueId, requestId);
            if (fresh is null)
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.REQUEST_NOT_FOUND, "Request not found");
            }

            var current = (fresh.GetString("Status") ?? "").Trim();
            if (string.Equals(current, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                cancelledRequest = fresh;
                request = fresh;
                return;
            }

            if (!string.Equals(current, "Pending", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(current, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.INVALID_STATUS_TRANSITION,
                    $"Only pending or approved practice requests can be cancelled (status: {current}).");
            }

            fresh["Status"] = "Cancelled";
            fresh["ReviewedUtc"] = now;
            fresh["ReviewedBy"] = reviewedBy;
            fresh["ReviewReason"] = reviewReason;
            fresh["UpdatedUtc"] = now;
            await _practiceRequestRepo.UpdateRequestAsync(fresh, fresh.ETag);
            cancelledRequest = fresh;
            request = fresh;
        });

        await ReleaseCancelledRequestSlotsAsync(leagueId, cancelledRequest!, now);
        return cancelledRequest!;
    }

    private async Task<List<string>> ConfirmPracticeRequestSlotsAsync(
        string leagueId,
        string division,
        string teamId,
        string requestId,
        TableEntity approvedRequest,
        string approvedByUserId,
        DateTimeOffset nowUtc)
    {
        var slotId = (approvedRequest.GetString("SlotId") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_INPUT,
                "Request is missing slotId.");
        }

        var representativeSlot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
        if (representativeSlot is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND, "Slot not found");
        }

        if (CanUseRecurringPatternApproval(representativeSlot))
        {
            var recurringConfirmed = await ConfirmRecurringPatternSlotsAsync(
                leagueId,
                division,
                teamId,
                requestId,
                approvedRequest,
                representativeSlot,
                approvedByUserId,
                nowUtc);
            if (recurringConfirmed.Count > 0)
            {
                return recurringConfirmed;
            }

            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                "No matching recurring practice weeks could be confirmed for this request.");
        }

        await ConfirmSingleRequestedSlotAsync(representativeSlot, requestId, teamId, approvedRequest, approvedByUserId, nowUtc);
        return new List<string> { representativeSlot.RowKey };
    }

    private async Task<List<string>> ConfirmRecurringPatternSlotsAsync(
        string leagueId,
        string division,
        string teamId,
        string requestId,
        TableEntity approvedRequest,
        TableEntity representativeSlot,
        string approvedByUserId,
        DateTimeOffset nowUtc)
    {
        var representativeSlotId = representativeSlot.RowKey;
        var patternDate = SlotEntityUtil.ReadString(representativeSlot, "GameDate");
        var patternFieldKey = SlotEntityUtil.ReadString(representativeSlot, "FieldKey");
        var patternStart = SlotEntityUtil.ReadString(representativeSlot, "StartTime");
        var patternEnd = SlotEntityUtil.ReadString(representativeSlot, "EndTime");
        if (!DateOnly.TryParse(patternDate, out var startDate))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_INPUT,
                "Requested slot has invalid GameDate for recurring approval.");
        }
        if (!TimeUtil.IsValidRange(patternStart, patternEnd, out var patternStartMin, out var patternEndMin, out _))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_INPUT,
                "Requested slot has invalid StartTime/EndTime for recurring approval.");
        }

        var weekday = (int)startDate.DayOfWeek;
        var divisionSlots = await QueryAllSlotsAsync(new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            FromDate = patternDate,
            PageSize = 500
        });

        var candidates = divisionSlots
            .Where(slot => MatchesRecurringPattern(slot, patternFieldKey, patternStart, patternEnd, weekday, patternDate))
            .OrderBy(slot => SlotEntityUtil.ReadString(slot, "GameDate"))
            .ThenBy(slot => SlotEntityUtil.ReadString(slot, "StartTime"))
            .ThenBy(slot => slot.RowKey)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates.Add(representativeSlot);
        }

        var lastDate = candidates
            .Select(slot => SlotEntityUtil.ReadString(slot, "GameDate"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .DefaultIfEmpty(patternDate)
            .Max() ?? patternDate;

        var existingConfirmedTeamSlots = await QueryAllSlotsAsync(new SlotQueryFilter
        {
            LeagueId = leagueId,
            Status = Constants.Status.SlotConfirmed,
            FromDate = patternDate,
            ToDate = lastDate,
            PageSize = 500
        });
        var moveFromRequestId = (approvedRequest.GetString("MoveFromRequestId") ?? "").Trim();

        var confirmedTeamCommitments = existingConfirmedTeamSlots
            .Where(slot =>
                SlotInvolvesTeam(slot, teamId) &&
                !string.Equals((slot.GetString("ConfirmedRequestId") ?? "").Trim(), moveFromRequestId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var confirmedPracticeWeeks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var confirmedByDate = new Dictionary<string, List<(int startMin, int endMin, string slotId)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in confirmedTeamCommitments)
        {
            var gameDate = SlotEntityUtil.ReadString(slot, "GameDate");
            var start = SlotEntityUtil.ReadString(slot, "StartTime");
            var end = SlotEntityUtil.ReadString(slot, "EndTime");
            if (string.IsNullOrWhiteSpace(gameDate) || !TimeUtil.IsValidRange(start, end, out var sMin, out var eMin, out _))
                continue;

            if (!confirmedByDate.TryGetValue(gameDate, out var dayEntries))
            {
                dayEntries = new List<(int startMin, int endMin, string slotId)>();
                confirmedByDate[gameDate] = dayEntries;
            }
            dayEntries.Add((sMin, eMin, slot.RowKey));

            if (SlotEntityUtil.IsPractice(slot))
            {
                var weekKey = WeekKeyFromIsoDate(gameDate);
                if (!string.IsNullOrWhiteSpace(weekKey))
                {
                    confirmedPracticeWeeks.Add(weekKey);
                }
            }
        }

        var approvedWeekKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var confirmedSlotIds = new List<string>();
        var skippedCount = 0;

        foreach (var candidate in candidates)
        {
            var candidateId = candidate.RowKey;
            var gameDate = SlotEntityUtil.ReadString(candidate, "GameDate");
            var start = SlotEntityUtil.ReadString(candidate, "StartTime");
            var end = SlotEntityUtil.ReadString(candidate, "EndTime");
            if (string.IsNullOrWhiteSpace(gameDate) || !TimeUtil.IsValidRange(start, end, out var startMin, out var endMin, out _))
            {
                if (string.Equals(candidateId, representativeSlotId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_INPUT,
                        "Requested slot has invalid date/time.");
                }
                skippedCount++;
                continue;
            }

            var weekKey = WeekKeyFromIsoDate(gameDate);
            if (!string.IsNullOrWhiteSpace(weekKey))
            {
                if (approvedWeekKeys.Contains(weekKey) || confirmedPracticeWeeks.Contains(weekKey))
                {
                    skippedCount++;
                    continue;
                }
            }

            if (confirmedByDate.TryGetValue(gameDate, out var sameDayCommitments) &&
                sameDayCommitments.Any(x => TimeUtil.Overlaps(startMin, endMin, x.startMin, x.endMin)))
            {
                skippedCount++;
                continue;
            }

            var wasConfirmed = await TryConfirmRecurringCandidateAsync(
                leagueId,
                division,
                teamId,
                requestId,
                candidateId,
                representativeSlotId,
                approvedRequest,
                approvedByUserId,
                nowUtc);

            if (!wasConfirmed)
            {
                skippedCount++;
                continue;
            }

            confirmedSlotIds.Add(candidateId);
            if (!string.IsNullOrWhiteSpace(weekKey))
            {
                approvedWeekKeys.Add(weekKey);
                confirmedPracticeWeeks.Add(weekKey);
            }

            if (!confirmedByDate.TryGetValue(gameDate, out sameDayCommitments))
            {
                sameDayCommitments = new List<(int startMin, int endMin, string slotId)>();
                confirmedByDate[gameDate] = sameDayCommitments;
            }
            sameDayCommitments.Add((startMin, endMin, candidateId));
        }

        try
        {
            approvedRequest["ApprovalMode"] = "RecurringPattern";
            approvedRequest["ApprovedPatternSlots"] = confirmedSlotIds.Count;
            approvedRequest["ApprovedPatternWeeks"] = approvedWeekKeys.Count;
            approvedRequest["SkippedPatternWeeks"] = skippedCount;
            approvedRequest["UpdatedUtc"] = nowUtc;
            await _practiceRequestRepo.UpdateRequestAsync(approvedRequest, ETag.All);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist recurring approval summary for practice request {RequestId}", requestId);
        }

        return confirmedSlotIds;
    }

    private async Task<bool> TryConfirmRecurringCandidateAsync(
        string leagueId,
        string division,
        string teamId,
        string requestId,
        string slotId,
        string representativeSlotId,
        TableEntity approvedRequest,
        string approvedByUserId,
        DateTimeOffset nowUtc)
    {
        try
        {
            await RetryUtil.WithEtagRetryAsync(async () =>
            {
                var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
                if (slot is null)
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND, "Slot not found");
                }

                var status = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
                if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.SLOT_CANCELLED, "Slot is cancelled.");
                }

                var isRepresentative = string.Equals(slot.RowKey, representativeSlotId, StringComparison.OrdinalIgnoreCase);
                if (string.Equals(status, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
                {
                    var alreadyTeamId = (slot.GetString("ConfirmedTeamId") ?? "").Trim();
                    var alreadyRequestId = (slot.GetString("ConfirmedRequestId") ?? "").Trim();
                    if (string.Equals(alreadyTeamId, teamId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(alreadyRequestId, requestId, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                        "Slot has already been confirmed for another request.");
                }

                if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    var pendingRequestId = (slot.GetString("PendingRequestId") ?? "").Trim();
                    if (!string.Equals(pendingRequestId, requestId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                            isRepresentative
                                ? "Requested slot is reserved by another request."
                                : "Pattern week is reserved by another request.");
                    }
                }
                else if (!string.Equals(status, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                        $"Slot is not available (status: {status}).");
                }

                if (!SlotEntityUtil.IsPracticeRequestableAvailability(slot) &&
                    !string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.SLOT_NOT_AVAILABLE,
                        "Only open availability practice slots can be requested.");
                }

                ApplyPracticeSlotConfirmation(slot, requestId, teamId, approvedRequest, approvedByUserId, nowUtc);
                await _slotRepo.UpdateSlotAsync(slot, slot.ETag);
            });

            return true;
        }
        catch (ApiGuards.HttpError ex)
        {
            if (string.Equals(slotId, representativeSlotId, StringComparison.OrdinalIgnoreCase))
                throw;

            _logger.LogInformation("Skipping recurring practice approval candidate {SlotId}: {Code} {Message}", slotId, ex.Code, ex.Message);
            return false;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            if (string.Equals(slotId, representativeSlotId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                    "Requested slot became unavailable during approval.");
            }

            _logger.LogInformation(ex, "Skipping recurring practice approval candidate {SlotId} due to concurrency conflict", slotId);
            return false;
        }
    }

    private async Task ConfirmSingleRequestedSlotAsync(
        TableEntity slot,
        string requestId,
        string teamId,
        TableEntity approvedRequest,
        string approvedByUserId,
        DateTimeOffset nowUtc)
    {
        var leagueId = (approvedRequest.GetString("LeagueId") ?? "").Trim();
        var division = (approvedRequest.GetString("Division") ?? "").Trim();
        var slotId = slot.RowKey;
        await RetryUtil.WithEtagRetryAsync(async () =>
        {
            var fresh = slot;
            if (!string.IsNullOrWhiteSpace(leagueId) && !string.IsNullOrWhiteSpace(division) && !string.IsNullOrWhiteSpace(slotId))
            {
                fresh = await _slotRepo.GetSlotAsync(leagueId, division, slotId) ?? slot;
            }

            var status = (fresh.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.SLOT_CANCELLED, "Slot is cancelled.");
            }

            if (string.Equals(status, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
            {
                var confirmedRequestId = (fresh.GetString("ConfirmedRequestId") ?? "").Trim();
                if (!string.Equals(confirmedRequestId, requestId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                        "Slot has already been confirmed for another request.");
                }
            }
            else if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                var pendingRequestId = (fresh.GetString("PendingRequestId") ?? "").Trim();
                if (!string.Equals(pendingRequestId, requestId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                        "Slot is reserved by another request.");
                }
            }
            else if (!string.Equals(status, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                    $"Slot is not available (status: {status}).");
            }

            ApplyPracticeSlotConfirmation(fresh, requestId, teamId, approvedRequest, approvedByUserId, nowUtc);
            await _slotRepo.UpdateSlotAsync(fresh, fresh.ETag);
            slot = fresh;
        });
    }

    private async Task RejectCompetingPendingSlotRequestsAsync(
        string leagueId,
        string division,
        string approvedRequestId,
        string reviewedByUserId,
        DateTimeOffset nowUtc,
        IReadOnlyCollection<string> confirmedSlotIds)
    {
        foreach (var slotId in confirmedSlotIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var others = await _practiceRequestRepo.QuerySlotRequestsAsync(leagueId, division, slotId, PendingStatusOnly);
            foreach (var other in others)
            {
                if (string.Equals(other.RowKey, approvedRequestId, StringComparison.OrdinalIgnoreCase))
                    continue;

                other["Status"] = "Rejected";
                other["ReviewedUtc"] = nowUtc;
                other["ReviewedBy"] = reviewedByUserId;
                other["ReviewReason"] = "Another request for this slot was approved.";
                other["UpdatedUtc"] = nowUtc;

                try { await _practiceRequestRepo.UpdateRequestAsync(other, other.ETag); }
                catch (RequestFailedException ex)
                {
                    _logger.LogWarning(ex, "Failed to reject competing practice request {RequestId}", other.RowKey);
                }
            }
        }
    }

    private async Task FinalizeApprovedMoveSourceAsync(
        string leagueId,
        TableEntity approvedRequest,
        string reviewedByUserId,
        DateTimeOffset nowUtc)
    {
        var moveFromRequestId = (approvedRequest.GetString("MoveFromRequestId") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(moveFromRequestId))
            return;

        try
        {
            await CancelRequestCoreAsync(
                leagueId,
                moveFromRequestId,
                reviewedByUserId,
                $"Moved to {(approvedRequest.GetString("DisplayName") ?? approvedRequest.GetString("FieldName") ?? approvedRequest.GetString("SlotId") ?? "another practice slot").Trim()}",
                request: null);

            approvedRequest["MoveCompletedUtc"] = nowUtc;
            approvedRequest["UpdatedUtc"] = nowUtc;
            await _practiceRequestRepo.UpdateRequestAsync(approvedRequest, ETag.All);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Approved move request {RequestId} could not release source request {SourceRequestId}", approvedRequest.RowKey, moveFromRequestId);
            throw;
        }
    }

    private async Task ReleaseCancelledRequestSlotsAsync(
        string leagueId,
        TableEntity request,
        DateTimeOffset nowUtc)
    {
        var division = (request.GetString("Division") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(division))
            return;

        var slotsForRequest = await FindRequestSlotsAsync(leagueId, division, request.RowKey, request);
        foreach (var slot in slotsForRequest)
        {
            await RetryUtil.WithEtagRetryAsync(async () =>
            {
                var fresh = await _slotRepo.GetSlotAsync(leagueId, division, slot.RowKey) ?? slot;
                var pendingRequestId = (fresh.GetString("PendingRequestId") ?? "").Trim();
                var confirmedRequestId = (fresh.GetString("ConfirmedRequestId") ?? "").Trim();
                var slotStatus = (fresh.GetString("Status") ?? Constants.Status.SlotOpen).Trim();

                var ownsPendingReservation = string.Equals(slotStatus, "Pending", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(pendingRequestId, request.RowKey, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(pendingRequestId));
                var ownsConfirmedReservation = string.Equals(slotStatus, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(confirmedRequestId, request.RowKey, StringComparison.OrdinalIgnoreCase);
                if (!ownsPendingReservation && !ownsConfirmedReservation)
                {
                    return;
                }

                var nextPending = (await _practiceRequestRepo.QuerySlotRequestsAsync(leagueId, division, slot.RowKey, PendingStatusOnly))
                    .Where(candidate => !string.Equals(candidate.RowKey, request.RowKey, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(candidate => candidate.GetDateTimeOffset("RequestedUtc") ?? DateTimeOffset.MaxValue)
                    .FirstOrDefault();

                if (nextPending is not null)
                {
                    fresh["Status"] = "Pending";
                    fresh["PendingRequestId"] = nextPending.RowKey;
                    fresh["PendingTeamId"] = (nextPending.GetString("TeamId") ?? "").Trim();
                    fresh["ConfirmedRequestId"] = "";
                    fresh["ConfirmedTeamId"] = "";
                    fresh["ConfirmedBy"] = "";
                    fresh["ConfirmedUtc"] = null;
                }
                else
                {
                    ResetPracticeSlotToAvailability(fresh, nowUtc);
                }

                fresh["UpdatedUtc"] = nowUtc;
                await _slotRepo.UpdateSlotAsync(fresh, fresh.ETag);
            });
        }
    }

    private async Task<List<TableEntity>> FindRequestSlotsAsync(
        string leagueId,
        string division,
        string requestId,
        TableEntity request)
    {
        var directSlotId = (request.GetString("SlotId") ?? "").Trim();
        var confirmedSlots = await QueryAllSlotsAsync(new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            Status = Constants.Status.SlotConfirmed,
            PageSize = 500
        });

        var matches = confirmedSlots
            .Where(slot => string.Equals((slot.GetString("ConfirmedRequestId") ?? "").Trim(), requestId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count > 0)
        {
            return matches;
        }

        if (string.IsNullOrWhiteSpace(directSlotId))
        {
            return [];
        }

        var directSlot = await _slotRepo.GetSlotAsync(leagueId, division, directSlotId);
        return directSlot is null ? [] : [directSlot];
    }

    private async Task<List<TableEntity>> QueryAllSlotsAsync(SlotQueryFilter filter)
    {
        var all = new List<TableEntity>();
        string? continuation = null;
        do
        {
            var page = await _slotRepo.QuerySlotsAsync(filter, continuation);
            if (page.Items.Count > 0)
                all.AddRange(page.Items);
            continuation = page.ContinuationToken;
        } while (!string.IsNullOrWhiteSpace(continuation));
        return all;
    }

    private static void ApplyPracticeSlotConfirmation(
        TableEntity slot,
        string requestId,
        string teamId,
        TableEntity approvedRequest,
        string approvedByUserId,
        DateTimeOffset nowUtc)
    {
        var openToShareField = approvedRequest.GetBoolean("OpenToShareField") ?? false;
        var shareWithTeamId = (approvedRequest.GetString("ShareWithTeamId") ?? "").Trim();

        slot["Status"] = Constants.Status.SlotConfirmed;
        slot["ConfirmedRequestId"] = requestId;
        slot["ConfirmedTeamId"] = teamId;
        slot["ConfirmedBy"] = approvedByUserId;
        slot["ConfirmedUtc"] = nowUtc;
        slot["PendingRequestId"] = "";
        slot["PendingTeamId"] = "";
        slot["OfferingTeamId"] = teamId;
        slot["IsAvailability"] = false;
        slot["GameType"] = "Practice";
        slot["PracticeBookingMode"] = "RecurringApproved";
        slot["OpenToShareField"] = openToShareField;
        slot["ShareWithTeamId"] = openToShareField ? shareWithTeamId : "";
        slot["PracticeShareable"] = true;
        slot["PracticeMaxTeamsPerBooking"] = 2;
        slot["PracticeReservedTeamIds"] = openToShareField && !string.IsNullOrWhiteSpace(shareWithTeamId)
            ? $"{teamId},{shareWithTeamId}"
            : teamId;
        slot["UpdatedUtc"] = nowUtc;
    }

    private static void ResetPracticeSlotToAvailability(TableEntity slot, DateTimeOffset nowUtc)
    {
        slot["Status"] = Constants.Status.SlotOpen;
        slot["ConfirmedRequestId"] = "";
        slot["ConfirmedTeamId"] = "";
        slot["ConfirmedBy"] = "";
        slot["ConfirmedUtc"] = null;
        slot["PendingRequestId"] = "";
        slot["PendingTeamId"] = "";
        slot["OfferingTeamId"] = "";
        slot["HomeTeamId"] = "";
        slot["AwayTeamId"] = "";
        slot["OfferingEmail"] = "";
        slot["IsAvailability"] = true;
        slot["GameType"] = "Availability";
        slot["PracticeBookingMode"] = "";
        slot["OpenToShareField"] = false;
        slot["ShareWithTeamId"] = "";
        slot["PracticeReservedTeamIds"] = "";
        slot["UpdatedUtc"] = nowUtc;
    }

    private static void CaptureRequestSlotSnapshot(TableEntity request, TableEntity slot)
    {
        request["GameDate"] = (slot.GetString("GameDate") ?? "").Trim();
        request["StartTime"] = (slot.GetString("StartTime") ?? "").Trim();
        request["EndTime"] = (slot.GetString("EndTime") ?? "").Trim();
        request["FieldKey"] = (slot.GetString("FieldKey") ?? "").Trim();
        request["FieldName"] = (slot.GetString("FieldName") ?? "").Trim();
        request["DisplayName"] = (slot.GetString("DisplayName") ?? "").Trim();
        request["PracticeSeasonLabel"] = (slot.GetString("PracticeSeasonLabel") ?? "").Trim();
        request["PracticeBookingPolicy"] = (slot.GetString("PracticeBookingPolicy") ?? "").Trim();
        request["PracticeSlotKey"] = (slot.GetString("PracticeSlotKey") ?? "").Trim();
        request["PracticeSourceRecordId"] = (slot.GetString("PracticeSourceRecordId") ?? "").Trim();
    }

    private static bool CanUseRecurringPatternApproval(TableEntity slot)
    {
        var gameDate = SlotEntityUtil.ReadString(slot, "GameDate");
        var fieldKey = SlotEntityUtil.ReadString(slot, "FieldKey");
        var start = SlotEntityUtil.ReadString(slot, "StartTime");
        var end = SlotEntityUtil.ReadString(slot, "EndTime");
        return !string.IsNullOrWhiteSpace(gameDate)
            && !string.IsNullOrWhiteSpace(fieldKey)
            && !string.IsNullOrWhiteSpace(start)
            && !string.IsNullOrWhiteSpace(end)
            && DateOnly.TryParse(gameDate, out _)
            && TimeUtil.IsValidRange(start, end, out _, out _, out _);
    }

    private static bool MatchesRecurringPattern(
        TableEntity slot,
        string fieldKey,
        string startTime,
        string endTime,
        int weekday,
        string fromDate)
    {
        var gameDate = SlotEntityUtil.ReadString(slot, "GameDate");
        if (string.IsNullOrWhiteSpace(gameDate) || string.CompareOrdinal(gameDate, fromDate) < 0)
            return false;
        if (!DateOnly.TryParse(gameDate, out var date))
            return false;
        if ((int)date.DayOfWeek != weekday)
            return false;

        var slotFieldKey = SlotEntityUtil.ReadString(slot, "FieldKey");
        var slotStart = SlotEntityUtil.ReadString(slot, "StartTime");
        var slotEnd = SlotEntityUtil.ReadString(slot, "EndTime");
        if (!string.Equals(slotFieldKey, fieldKey, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(slotStart, startTime, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(slotEnd, endTime, StringComparison.OrdinalIgnoreCase))
            return false;

        var status = SlotEntityUtil.ReadString(slot, "Status", Constants.Status.SlotOpen);
        if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool SlotInvolvesTeam(TableEntity slot, string teamId)
    {
        var offeringTeamId = (slot.GetString("OfferingTeamId") ?? "").Trim();
        var confirmedTeamId = (slot.GetString("ConfirmedTeamId") ?? "").Trim();
        return string.Equals(offeringTeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(confirmedTeamId, teamId, StringComparison.OrdinalIgnoreCase);
    }

    private static string WeekKeyFromIsoDate(string isoDate)
    {
        if (!DateOnly.TryParse(isoDate, out var date))
            return "";

        var dt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var week = System.Globalization.ISOWeek.GetWeekOfYear(dt);
        return $"{System.Globalization.ISOWeek.GetYear(dt)}-W{week:00}";
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

    private async Task EnsureRequestOwnerOrAdminAsync(string leagueId, string userId, TableEntity request)
    {
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
            return;

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
        if (string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            return;

        var teamId = (request.GetString("TeamId") ?? "").Trim();
        var division = (request.GetString("Division") ?? "").Trim();
        var coachTeamId = ReadMembershipTeamId(membership);
        var coachDivision = ReadMembershipDivision(membership);
        if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(coachTeamId) ||
            string.IsNullOrWhiteSpace(coachDivision) ||
            !string.Equals(coachTeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(coachDivision, division, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                "You can only manage practice requests for your assigned team.");
        }
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
        return (membership?.GetString("Division") ?? "").Trim();
    }

    private static string ReadMembershipTeamId(TableEntity? membership)
    {
        return (membership?.GetString("TeamId") ?? "").Trim();
    }

    private static string PracticeRequestPk(string leagueId) => $"PRACTICEREQ|{leagueId}";
}
