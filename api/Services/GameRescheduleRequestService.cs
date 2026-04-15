using System.Globalization;
using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service for game reschedule request workflows.
/// Handles two-team approval and atomic game slot transitions.
/// </summary>
public class GameRescheduleRequestService : IGameRescheduleRequestService
{
    private static readonly string[] ActiveStatuses = { GameRescheduleRequestStatuses.PendingOpponent, GameRescheduleRequestStatuses.ApprovedByBothTeams };
    private const int MinimumLeadTimeHours = 72;

    private readonly IGameRescheduleRequestRepository _requestRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger<GameRescheduleRequestService> _logger;

    public GameRescheduleRequestService(
        IGameRescheduleRequestRepository requestRepo,
        ISlotRepository slotRepo,
        IMembershipRepository membershipRepo,
        ILogger<GameRescheduleRequestService> logger)
    {
        _requestRepo = requestRepo;
        _slotRepo = slotRepo;
        _membershipRepo = membershipRepo;
        _logger = logger;
    }

    public async Task<TableEntity> CreateRescheduleRequestAsync(
        string leagueId,
        string userId,
        string division,
        string originalSlotId,
        string proposedSlotId,
        string reason)
    {
        division = (division ?? "").Trim();
        originalSlotId = (originalSlotId ?? "").Trim();
        proposedSlotId = (proposedSlotId ?? "").Trim();
        reason = (reason ?? "").Trim();

        if (string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(originalSlotId) ||
            string.IsNullOrWhiteSpace(proposedSlotId) || string.IsNullOrWhiteSpace(reason))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "division, originalSlotId, proposedSlotId, and reason are required.");
        }

        ApiGuards.EnsureValidTableKeyPart("division", division);
        ApiGuards.EnsureValidTableKeyPart("originalSlotId", originalSlotId);
        ApiGuards.EnsureValidTableKeyPart("proposedSlotId", proposedSlotId);

        if (string.Equals(originalSlotId, proposedSlotId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "Proposed slot must be different from original slot.");
        }

        // Validate original slot (must be Confirmed game)
        var originalSlot = await _slotRepo.GetSlotAsync(leagueId, division, originalSlotId);
        if (originalSlot is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND,
                "Original game slot not found.");
        }

        var originalStatus = (originalSlot.GetString("Status") ?? "").Trim();
        if (!string.Equals(originalStatus, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.GAME_NOT_CONFIRMED,
                $"Only confirmed games can be rescheduled (current status: {originalStatus}).");
        }

        var isAvailability = originalSlot.GetBoolean("IsAvailability") ?? false;
        if (isAvailability)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "Availability slots cannot be rescheduled using this endpoint.");
        }

        // Verify user owns this game (HomeTeam or AwayTeam)
        var homeTeamId = (originalSlot.GetString("HomeTeamId") ?? "").Trim();
        var awayTeamId = (originalSlot.GetString("AwayTeamId") ?? "").Trim();

        if (string.IsNullOrWhiteSpace(homeTeamId) || string.IsNullOrWhiteSpace(awayTeamId))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "Game must have both home and away teams assigned.");
        }

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var isAdmin = await _membershipRepo.IsGlobalAdminAsync(userId) ||
                      string.Equals(membership?.GetString("Role"), Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);

        var userTeamId = (membership?.GetString("TeamId") ?? membership?.GetString("CoachTeamId") ?? "").Trim();
        var isHomeTeam = string.Equals(userTeamId, homeTeamId, StringComparison.OrdinalIgnoreCase);
        var isAwayTeam = string.Equals(userTeamId, awayTeamId, StringComparison.OrdinalIgnoreCase);

        if (!isAdmin && !isHomeTeam && !isAwayTeam)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.NOT_GAME_PARTICIPANT,
                "Only teams involved in the game can request a reschedule.");
        }

        // Determine requesting and opponent teams
        string requestingTeamId, opponentTeamId;
        if (isHomeTeam || (isAdmin && !isAwayTeam))
        {
            requestingTeamId = homeTeamId;
            opponentTeamId = awayTeamId;
        }
        else
        {
            requestingTeamId = awayTeamId;
            opponentTeamId = homeTeamId;
        }

        // Enforce 72-hour lead time
        ValidateLeadTime(originalSlot, MinimumLeadTimeHours);

        // Check for existing active reschedule request
        var hasActiveRequest = await _requestRepo.HasActiveRequestForSlotAsync(leagueId, division, originalSlotId, ActiveStatuses);
        if (hasActiveRequest)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                "An active reschedule request already exists for this game.");
        }

        // Validate proposed slot (must be Open)
        var proposedSlot = await _slotRepo.GetSlotAsync(leagueId, division, proposedSlotId);
        if (proposedSlot is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND,
                "Proposed slot not found.");
        }

        var proposedStatus = (proposedSlot.GetString("Status") ?? "").Trim();
        if (!string.Equals(proposedStatus, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.SLOT_NOT_OPEN,
                $"Proposed slot must be Open (current status: {proposedStatus}).");
        }

        // Check conflicts for both teams
        var conflictCheck = await CheckConflictsAsync(leagueId, division, originalSlotId, proposedSlotId);
        if (conflictCheck.HomeTeamHasConflicts || conflictCheck.AwayTeamHasConflicts)
        {
            var totalConflicts = conflictCheck.HomeTeamConflicts.Count + conflictCheck.AwayTeamConflicts.Count;
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.RESCHEDULE_CONFLICT_DETECTED,
                $"Reschedule would create {totalConflicts} schedule conflict(s). Check conflicts endpoint for details.");
        }

        // Create reschedule request
        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.NewGuid().ToString();
        var request = new TableEntity($"GAMERESCHEDULE|{leagueId}", requestId)
        {
            ["LeagueId"] = leagueId,
            ["Division"] = division,
            ["OriginalSlotId"] = originalSlotId,
            ["ProposedSlotId"] = proposedSlotId,
            ["RequestingTeamId"] = requestingTeamId,
            ["OpponentTeamId"] = opponentTeamId,
            ["RequestingCoachUserId"] = userId,
            ["Reason"] = reason,
            ["Status"] = GameRescheduleRequestStatuses.PendingOpponent,
            ["RequestedUtc"] = now,
            ["UpdatedUtc"] = now,

            // Snapshot of original game
            ["OriginalGameDate"] = originalSlot.GetString("GameDate"),
            ["OriginalStartTime"] = originalSlot.GetString("StartTime"),
            ["OriginalEndTime"] = originalSlot.GetString("EndTime"),
            ["OriginalFieldKey"] = originalSlot.GetString("FieldKey"),
            ["OriginalFieldName"] = originalSlot.GetString("DisplayName") ?? originalSlot.GetString("FieldName"),

            // Snapshot of proposed slot
            ["ProposedGameDate"] = proposedSlot.GetString("GameDate"),
            ["ProposedStartTime"] = proposedSlot.GetString("StartTime"),
            ["ProposedEndTime"] = proposedSlot.GetString("EndTime"),
            ["ProposedFieldKey"] = proposedSlot.GetString("FieldKey"),
            ["ProposedFieldName"] = proposedSlot.GetString("DisplayName") ?? proposedSlot.GetString("FieldName"),
        };

        await _requestRepo.CreateRequestAsync(request);

        // TODO: Trigger opponent notification
        _logger.LogInformation("Game reschedule request created: {RequestId} for slot {OriginalSlotId} → {ProposedSlotId}",
            requestId, originalSlotId, proposedSlotId);

        return request;
    }

    public async Task<TableEntity> OpponentApproveAsync(
        string leagueId,
        string userId,
        string requestId,
        string? response)
    {
        var request = await _requestRepo.GetRequestAsync(leagueId, requestId);
        if (request is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.GAME_RESCHEDULE_NOT_FOUND,
                "Reschedule request not found.");
        }

        var status = (request.GetString("Status") ?? "").Trim();
        if (!string.Equals(status, GameRescheduleRequestStatuses.PendingOpponent, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.INVALID_STATUS_TRANSITION,
                $"Request cannot be approved (current status: {status}).");
        }

        // Verify user is opponent team coach or admin
        var opponentTeamId = (request.GetString("OpponentTeamId") ?? "").Trim();
        await EnsureOpponentAuthorization(userId, leagueId, opponentTeamId);

        // Update request status
        request["Status"] = GameRescheduleRequestStatuses.ApprovedByBothTeams;
        request["OpponentApprovedUtc"] = DateTimeOffset.UtcNow;
        request["OpponentApprovedBy"] = userId;
        request["OpponentResponse"] = response ?? "";
        request["UpdatedUtc"] = DateTimeOffset.UtcNow;

        await _requestRepo.UpdateRequestAsync(request, request.ETag);

        // TODO: Notify requesting team

        // Auto-finalize (could be made configurable per league)
        await FinalizeAsync(leagueId, userId, requestId);

        return request;
    }

    public async Task<TableEntity> OpponentRejectAsync(
        string leagueId,
        string userId,
        string requestId,
        string? response)
    {
        var request = await _requestRepo.GetRequestAsync(leagueId, requestId);
        if (request is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.GAME_RESCHEDULE_NOT_FOUND,
                "Reschedule request not found.");
        }

        var status = (request.GetString("Status") ?? "").Trim();
        if (!string.Equals(status, GameRescheduleRequestStatuses.PendingOpponent, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.INVALID_STATUS_TRANSITION,
                $"Request cannot be rejected (current status: {status}).");
        }

        // Verify user is opponent team coach or admin
        var opponentTeamId = (request.GetString("OpponentTeamId") ?? "").Trim();
        await EnsureOpponentAuthorization(userId, leagueId, opponentTeamId);

        // Update request status
        request["Status"] = GameRescheduleRequestStatuses.Rejected;
        request["OpponentApprovedUtc"] = DateTimeOffset.UtcNow;
        request["OpponentApprovedBy"] = userId;
        request["OpponentResponse"] = response ?? "";
        request["UpdatedUtc"] = DateTimeOffset.UtcNow;

        await _requestRepo.UpdateRequestAsync(request, request.ETag);

        // TODO: Notify requesting team of rejection

        return request;
    }

    public async Task<TableEntity> FinalizeAsync(
        string leagueId,
        string userId,
        string requestId)
    {
        var request = await _requestRepo.GetRequestAsync(leagueId, requestId);
        if (request is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.GAME_RESCHEDULE_NOT_FOUND,
                "Reschedule request not found.");
        }

        var status = (request.GetString("Status") ?? "").Trim();
        if (!string.Equals(status, GameRescheduleRequestStatuses.ApprovedByBothTeams, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.INVALID_STATUS_TRANSITION,
                $"Only approved requests can be finalized (current status: {status}).");
        }

        var division = (request.GetString("Division") ?? "").Trim();
        var originalSlotId = (request.GetString("OriginalSlotId") ?? "").Trim();
        var proposedSlotId = (request.GetString("ProposedSlotId") ?? "").Trim();

        // CRITICAL: Atomic operation to cancel original and confirm proposed
        try
        {
            await RetryUtil.WithEtagRetryAsync(async () =>
            {
                // Step 1: Cancel original game
                var originalSlot = await _slotRepo.GetSlotAsync(leagueId, division, originalSlotId);
                if (originalSlot is null)
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND,
                        "Original game slot not found.");
                }

                var originalStatus = (originalSlot.GetString("Status") ?? "").Trim();
                if (!string.Equals(originalStatus, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.CONFLICT,
                        $"Original game is no longer confirmed (status: {originalStatus}).");
                }

                originalSlot["Status"] = Constants.Status.SlotCancelled;
                originalSlot["CancelledReason"] = $"Rescheduled to {request.GetString("ProposedGameDate")} {request.GetString("ProposedStartTime")} at {request.GetString("ProposedFieldName")}";
                originalSlot["UpdatedUtc"] = DateTimeOffset.UtcNow;
                originalSlot["UpdatedBy"] = userId;

                await _slotRepo.UpdateSlotAsync(originalSlot, originalSlot.ETag);

                // Step 2: Confirm proposed slot with game metadata
                var proposedSlot = await _slotRepo.GetSlotAsync(leagueId, division, proposedSlotId);
                if (proposedSlot is null)
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND,
                        "Proposed slot not found.");
                }

                var proposedStatus = (proposedSlot.GetString("Status") ?? "").Trim();
                if (!string.Equals(proposedStatus, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.SLOT_NOT_OPEN,
                        $"Proposed slot is no longer available (status: {proposedStatus}).");
                }

                // Copy game metadata from original to proposed
                proposedSlot["Status"] = Constants.Status.SlotConfirmed;
                proposedSlot["HomeTeamId"] = originalSlot.GetString("HomeTeamId");
                proposedSlot["AwayTeamId"] = originalSlot.GetString("AwayTeamId");
                proposedSlot["GameType"] = originalSlot.GetString("GameType");
                proposedSlot["Notes"] = $"Rescheduled from {request.GetString("OriginalGameDate")}";
                proposedSlot["UpdatedUtc"] = DateTimeOffset.UtcNow;
                proposedSlot["UpdatedBy"] = userId;

                await _slotRepo.UpdateSlotAsync(proposedSlot, proposedSlot.ETag);
            });

            // Step 3: Update request status
            request["Status"] = GameRescheduleRequestStatuses.Finalized;
            request["FinalizedUtc"] = DateTimeOffset.UtcNow;
            request["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await _requestRepo.UpdateRequestAsync(request, ETag.All);

            // TODO: Notify both teams of finalization

            _logger.LogInformation("Game reschedule finalized: {RequestId}", requestId);

            return request;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize game reschedule request {RequestId}", requestId);
            throw new ApiGuards.HttpError((int)HttpStatusCode.InternalServerError, ErrorCodes.FINALIZATION_FAILED,
                "Failed to finalize reschedule. Please contact an administrator.");
        }
    }

    public async Task<TableEntity> CancelAsync(
        string leagueId,
        string userId,
        string requestId)
    {
        var request = await _requestRepo.GetRequestAsync(leagueId, requestId);
        if (request is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.GAME_RESCHEDULE_NOT_FOUND,
                "Reschedule request not found.");
        }

        var status = (request.GetString("Status") ?? "").Trim();
        if (string.Equals(status, GameRescheduleRequestStatuses.Finalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.INVALID_STATUS_TRANSITION,
                "Cannot cancel a finalized reschedule request.");
        }

        // Verify user is requesting team or admin
        var requestingTeamId = (request.GetString("RequestingTeamId") ?? "").Trim();
        await EnsureRequestingTeamAuthorization(userId, leagueId, requestingTeamId);

        // Update request status
        request["Status"] = GameRescheduleRequestStatuses.Cancelled;
        request["UpdatedUtc"] = DateTimeOffset.UtcNow;

        await _requestRepo.UpdateRequestAsync(request, request.ETag);

        // TODO: Notify opponent team of cancellation

        return request;
    }

    public async Task<List<TableEntity>> QueryRequestsAsync(
        string leagueId,
        string userId,
        string? status)
    {
        // Get user's team to filter requests
        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var isAdmin = await _membershipRepo.IsGlobalAdminAsync(userId) ||
                      string.Equals(membership?.GetString("Role"), Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);

        string? teamFilter = null;
        if (!isAdmin)
        {
            var userTeamId = (membership?.GetString("TeamId") ?? membership?.GetString("CoachTeamId") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(userTeamId))
            {
                return new List<TableEntity>();
            }
            teamFilter = userTeamId;
        }

        return await _requestRepo.QueryRequestsAsync(leagueId, status, teamFilter);
    }

    public async Task<GameRescheduleConflictCheckResponse> CheckConflictsAsync(
        string leagueId,
        string division,
        string originalSlotId,
        string proposedSlotId)
    {
        // Get both slots
        var originalSlot = await _slotRepo.GetSlotAsync(leagueId, division, originalSlotId);
        if (originalSlot is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND,
                "Original slot not found.");
        }

        var proposedSlot = await _slotRepo.GetSlotAsync(leagueId, division, proposedSlotId);
        if (proposedSlot is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.SLOT_NOT_FOUND,
                "Proposed slot not found.");
        }

        var homeTeamId = (originalSlot.GetString("HomeTeamId") ?? "").Trim();
        var awayTeamId = (originalSlot.GetString("AwayTeamId") ?? "").Trim();
        var proposedDate = (proposedSlot.GetString("GameDate") ?? "").Trim();
        var proposedStartTime = (proposedSlot.GetString("StartTime") ?? "").Trim();
        var proposedEndTime = (proposedSlot.GetString("EndTime") ?? "").Trim();

        if (!TimeUtil.TryParseMinutes(proposedStartTime, out var proposedStartMin) ||
            !TimeUtil.TryParseMinutes(proposedEndTime, out var proposedEndMin))
        {
            return new GameRescheduleConflictCheckResponse(false, false, new(), new());
        }

        // Check conflicts for both teams
        var homeTeamConflicts = await FindTeamConflicts(leagueId, division, homeTeamId, proposedDate, proposedStartMin, proposedEndMin, proposedSlotId);
        var awayTeamConflicts = await FindTeamConflicts(leagueId, division, awayTeamId, proposedDate, proposedStartMin, proposedEndMin, proposedSlotId);

        return new GameRescheduleConflictCheckResponse(
            homeTeamConflicts.Count > 0,
            awayTeamConflicts.Count > 0,
            homeTeamConflicts,
            awayTeamConflicts);
    }

    private async Task<List<GameRescheduleConflictDto>> FindTeamConflicts(
        string leagueId,
        string division,
        string teamId,
        string proposedDate,
        int proposedStartMin,
        int proposedEndMin,
        string excludeSlotId)
    {
        var conflicts = new List<GameRescheduleConflictDto>();

        // Query all confirmed slots for this team on the proposed date
        var slotsOnDate = await _slotRepo.QuerySlotsAsync(new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            FromDate = proposedDate,
            ToDate = proposedDate,
            Statuses = new List<string> { Constants.Status.SlotConfirmed },
            ExcludeAvailability = true,
            PageSize = 100
        });

        foreach (var slot in slotsOnDate.Items)
        {
            // Skip the proposed slot itself
            if (string.Equals(slot.RowKey, excludeSlotId, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if team is involved in this slot
            var homeId = (slot.GetString("HomeTeamId") ?? "").Trim();
            var awayId = (slot.GetString("AwayTeamId") ?? "").Trim();
            var offeringId = (slot.GetString("OfferingTeamId") ?? "").Trim();
            var confirmedId = (slot.GetString("ConfirmedTeamId") ?? "").Trim();

            var teamInvolved =
                string.Equals(teamId, homeId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(teamId, awayId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(teamId, offeringId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(teamId, confirmedId, StringComparison.OrdinalIgnoreCase);

            if (!teamInvolved)
                continue;

            // Check time overlap
            var slotStart = (slot.GetString("StartTime") ?? "").Trim();
            var slotEnd = (slot.GetString("EndTime") ?? "").Trim();

            if (!TimeUtil.TryParseMinutes(slotStart, out var slotStartMin) ||
                !TimeUtil.TryParseMinutes(slotEnd, out var slotEndMin))
                continue;

            if (!TimeUtil.Overlaps(proposedStartMin, proposedEndMin, slotStartMin, slotEndMin))
                continue;

            // Found a conflict
            var isGame = !(slot.GetBoolean("IsAvailability") ?? false);
            var opponent = "";

            if (isGame)
            {
                if (string.Equals(homeId, teamId, StringComparison.OrdinalIgnoreCase))
                    opponent = awayId;
                else if (string.Equals(awayId, teamId, StringComparison.OrdinalIgnoreCase))
                    opponent = homeId;
            }

            conflicts.Add(new GameRescheduleConflictDto(
                Type: isGame ? "game" : "practice",
                Date: proposedDate,
                StartTime: slotStart,
                EndTime: slotEnd,
                Location: (slot.GetString("DisplayName") ?? slot.GetString("FieldName") ?? "Unknown").Trim(),
                Opponent: string.IsNullOrWhiteSpace(opponent) ? null : opponent,
                Status: (slot.GetString("Status") ?? "").Trim()));
        }

        return conflicts;
    }

    private void ValidateLeadTime(TableEntity slot, int minimumHours)
    {
        var gameDate = (slot.GetString("GameDate") ?? "").Trim();
        var startTime = (slot.GetString("StartTime") ?? "").Trim();

        if (string.IsNullOrWhiteSpace(gameDate) || string.IsNullOrWhiteSpace(startTime))
            return;

        if (DateTime.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) &&
            TimeUtil.TryParseMinutes(startTime, out var startMin))
        {
            var gameDateTime = parsedDate.AddMinutes(startMin);
            var hoursUntil = (gameDateTime - DateTime.UtcNow).TotalHours;

            if (hoursUntil < minimumHours && hoursUntil > 0)
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.LEAD_TIME_VIOLATION,
                    $"Game cannot be rescheduled within {minimumHours} hours of the scheduled time. This game is in {Math.Round(hoursUntil, 1)} hours.");
            }
        }
    }

    private async Task EnsureOpponentAuthorization(string userId, string leagueId, string opponentTeamId)
    {
        var isAdmin = await _membershipRepo.IsGlobalAdminAsync(userId);
        if (isAdmin) return;

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? "").Trim();

        if (string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            return;

        var userTeamId = (membership?.GetString("TeamId") ?? membership?.GetString("CoachTeamId") ?? "").Trim();

        if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(userTeamId, opponentTeamId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                "Only the opponent team coach can approve or reject this request.");
        }
    }

    private async Task EnsureRequestingTeamAuthorization(string userId, string leagueId, string requestingTeamId)
    {
        var isAdmin = await _membershipRepo.IsGlobalAdminAsync(userId);
        if (isAdmin) return;

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? "").Trim();

        if (string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            return;

        var userTeamId = (membership?.GetString("TeamId") ?? membership?.GetString("CoachTeamId") ?? "").Trim();

        if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(userTeamId, requestingTeamId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                "Only the requesting team coach can cancel this request.");
        }
    }
}
