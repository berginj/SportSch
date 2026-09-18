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
    private readonly INotificationService _notificationService;
    private readonly ILogger<GameRescheduleRequestService> _logger;
    private readonly TimeProvider _clock;

    public GameRescheduleRequestService(
        IGameRescheduleRequestRepository requestRepo,
        ISlotRepository slotRepo,
        IMembershipRepository membershipRepo,
        INotificationService notificationService,
        ILogger<GameRescheduleRequestService> logger, TimeProvider? clock = null)
    {
        _requestRepo = requestRepo;
        _slotRepo = slotRepo;
        _membershipRepo = membershipRepo;
        _notificationService = notificationService;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
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
        ApiGuards.EnsureMaxLength("reason", reason, ApiGuards.InputLimits.Reason);

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
        var awayTeamId = SlotEntityUtil.ReadOpponentTeamId(originalSlot);

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

        if (!isAdmin && (!string.Equals(membership?.GetString("Role"), Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(membership?.GetString("Division"), division, StringComparison.OrdinalIgnoreCase) || (!isHomeTeam && !isAwayTeam)))
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
        if (!string.Equals(proposedStatus, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase) ||
            !(proposedSlot.GetBoolean("IsAvailability") ?? false) || SlotEntityUtil.IsPractice(proposedSlot))
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
        var now = _clock.GetUtcNow();
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

        _logger.LogInformation("Game reschedule request created: {RequestId} for slot {OriginalSlotId} → {ProposedSlotId}",
            requestId, originalSlotId, proposedSlotId);

        // Notify opponent team (fire and forget - don't block response)
        await SendNotificationsAsync(async () =>
        {
            try
            {
                var proposedDate = proposedSlot.GetString("GameDate") ?? "";
                var proposedTime = proposedSlot.GetString("StartTime") ?? "";
                var proposedField = proposedSlot.GetString("DisplayName") ?? proposedSlot.GetString("FieldName") ?? "";
                var originalDate = originalSlot.GetString("GameDate") ?? "";
                var originalTime = originalSlot.GetString("StartTime") ?? "";

                // Get opponent team coaches
                var opponentMemberships = await _membershipRepo.GetLeagueMembershipsAsync(leagueId);
                var opponentCoaches = opponentMemberships
                    .Where(m =>
                    {
                        var role = (m.GetString("Role") ?? "").Trim();
                        var coachTeamId = (m.GetString("TeamId") ?? m.GetString("CoachTeamId") ?? "").Trim();
                        return string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(coachTeamId, opponentTeamId, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(m => m.PartitionKey)
                    .Distinct()
                    .ToList();

                var notificationTasks = new List<Task>();
                foreach (var opponentUserId in opponentCoaches)
                {
                    var message = $"{requestingTeamId} requested to reschedule your game from {originalDate} at {originalTime} to {proposedDate} at {proposedTime} at {proposedField}. Please review.";
                    notificationTasks.Add(_notificationService.CreateNotificationAsync(
                        opponentUserId,
                        leagueId,
                        "RescheduleRequested",
                        message,
                        "#notifications",
                        requestId,
                        "GameReschedule"));
                }

                await Task.WhenAll(notificationTasks);
                _logger.LogInformation("Sent {Count} reschedule notifications for request {RequestId}", notificationTasks.Count, requestId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reschedule notification for request {RequestId}", requestId);
            }
        });

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

        var opponentTeamId = (request.GetString("OpponentTeamId") ?? "").Trim();
        await EnsureOpponentAuthorization(userId, leagueId, request.GetString("Division") ?? "", opponentTeamId);
        if (request.GetString("Status") == GameRescheduleRequestStatuses.Finalized) return request;
        if (request.GetString("Status") == GameRescheduleRequestStatuses.ApprovedByBothTeams)
            return await FinalizeAsync(leagueId, userId, requestId);

        var status = (request.GetString("Status") ?? "").Trim();
        if (!string.Equals(status, GameRescheduleRequestStatuses.PendingOpponent, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.INVALID_STATUS_TRANSITION,
                $"Request cannot be approved (current status: {status}).");
        }

        // Update request status
        request["Status"] = GameRescheduleRequestStatuses.ApprovedByBothTeams;
        request["OpponentApprovedUtc"] = _clock.GetUtcNow();
        request["OpponentApprovedBy"] = userId;
        request["OpponentResponse"] = response ?? "";
        request["UpdatedUtc"] = _clock.GetUtcNow();

        await _requestRepo.UpdateRequestAsync(request, request.ETag);

        // Notify requesting team of approval (awaited)
        await SendNotificationsAsync(async () =>
        {
            try
            {
                var requestingTeamId = (request.GetString("RequestingTeamId") ?? "").Trim();
                var coaches = await GetCoachesForTeamAsync(leagueId, request.GetString("Division") ?? "", requestingTeamId);
                var proposedDate = request.GetString("ProposedGameDate") ?? "";
                var proposedTime = request.GetString("ProposedStartTime") ?? "";
                var proposedField = request.GetString("ProposedFieldName") ?? "";

                var tasks = coaches.Select(coachUserId =>
                    _notificationService.CreateNotificationAsync(
                        coachUserId, leagueId, "RescheduleApproved",
                        $"Your reschedule request was approved. Game will move to {proposedDate} at {proposedTime} at {proposedField}.",
                        "#calendar", requestId, "GameReschedule"));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reschedule approval notification for request {RequestId}", requestId);
            }
        });

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
        await EnsureOpponentAuthorization(userId, leagueId, request.GetString("Division") ?? "", opponentTeamId);

        // Update request status
        request["Status"] = GameRescheduleRequestStatuses.Rejected;
        request["OpponentApprovedUtc"] = _clock.GetUtcNow();
        request["OpponentApprovedBy"] = userId;
        request["OpponentResponse"] = response ?? "";
        request["UpdatedUtc"] = _clock.GetUtcNow();

        await _requestRepo.UpdateRequestAsync(request, request.ETag);

        // Notify requesting team of rejection (awaited)
        await SendNotificationsAsync(async () =>
        {
            try
            {
                var requestingTeamId = (request.GetString("RequestingTeamId") ?? "").Trim();
                var coaches = await GetCoachesForTeamAsync(leagueId, request.GetString("Division") ?? "", requestingTeamId);
                var originalDate = request.GetString("OriginalGameDate") ?? "";
                var originalTime = request.GetString("OriginalStartTime") ?? "";
                var opponentResponse = (response ?? "No reason given").Trim();

                var tasks = coaches.Select(coachUserId =>
                    _notificationService.CreateNotificationAsync(
                        coachUserId, leagueId, "RescheduleRejected",
                        $"Your reschedule request for {originalDate} at {originalTime} was declined. Reason: {opponentResponse}",
                        "#calendar", requestId, "GameReschedule"));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reschedule rejection notification for request {RequestId}", requestId);
            }
        });

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

        await EnsureOpponentAuthorization(userId, leagueId, request.GetString("Division") ?? "", request.GetString("OpponentTeamId") ?? "");
        if (request.GetString("Status") == GameRescheduleRequestStatuses.Finalized) return request;

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
            var originalSlot = await _slotRepo.GetSlotAsync(leagueId, division, originalSlotId)
                ?? throw new ApiGuards.HttpError(404, ErrorCodes.SLOT_NOT_FOUND, "Original game slot not found.");
            var proposedSlot = await _slotRepo.GetSlotAsync(leagueId, division, proposedSlotId)
                ?? throw new ApiGuards.HttpError(404, ErrorCodes.SLOT_NOT_FOUND, "Proposed slot not found.");
            var alreadyMoved = originalSlot.GetString("RescheduleOperationId") == requestId
                && proposedSlot.GetString("RescheduleOperationId") == requestId;
            if (!alreadyMoved)
            {
                if (originalSlot.GetString("Status") != Constants.Status.SlotConfirmed)
                    throw new ApiGuards.HttpError(409, ErrorCodes.CONFLICT, "The original game changed. Review the request again.");
                if (proposedSlot.GetString("Status") != Constants.Status.SlotOpen || !(proposedSlot.GetBoolean("IsAvailability") ?? false))
                    throw new ApiGuards.HttpError(409, ErrorCodes.SLOT_NOT_OPEN, "The replacement is no longer available. The original game has not changed.");
                ValidateLeadTime(originalSlot, MinimumLeadTimeHours);
                var currentConflicts = await CheckConflictsAsync(leagueId, division, originalSlotId, proposedSlotId);
                if (currentConflicts.HomeTeamHasConflicts || currentConflicts.AwayTeamHasConflicts)
                    throw new ApiGuards.HttpError(409, ErrorCodes.RESCHEDULE_CONFLICT_DETECTED,
                        "A team now has a conflicting booking. The original game has not changed.");
                originalSlot["Status"] = Constants.Status.SlotCancelled;
                originalSlot["CancelledReason"] = $"Rescheduled by request {requestId}";
                originalSlot["UpdatedUtc"] = _clock.GetUtcNow();
                originalSlot["UpdatedBy"] = userId;
                originalSlot["RescheduleOperationId"] = requestId;
                foreach (var property in new[] { "OfferingTeamId", "HomeTeamId", "AwayTeamId", "ConfirmedTeamId", "ConfirmedBy", "ConfirmedUtc", "GameType", "OfferingEmail" })
                    if (originalSlot.TryGetValue(property, out var value)) proposedSlot[property] = value;
                proposedSlot["MovedFromSlotId"] = originalSlotId;
                proposedSlot["MovedFromConfirmedRequestId"] = originalSlot.GetString("ConfirmedRequestId") ?? "";
                proposedSlot["ConfirmedRequestId"] = "";
                proposedSlot["Status"] = Constants.Status.SlotConfirmed;
                proposedSlot["IsAvailability"] = false;
                proposedSlot["IsExternalOffer"] = false;
                proposedSlot["Notes"] = $"{originalSlot.GetString("Notes")} | Rescheduled from {request.GetString("OriginalGameDate")}";
                proposedSlot["UpdatedUtc"] = _clock.GetUtcNow();
                proposedSlot["UpdatedBy"] = userId;
                proposedSlot["RescheduleOperationId"] = requestId;
                await _slotRepo.UpdateSlotsAtomicallyAsync(new[] { originalSlot, proposedSlot });
            }

            // Step 3: Update request status
            request["Status"] = GameRescheduleRequestStatuses.Finalized;
            request["FinalizedUtc"] = _clock.GetUtcNow();
            request["UpdatedUtc"] = _clock.GetUtcNow();

            await _requestRepo.UpdateRequestAsync(request, request.ETag);

            // Notify both teams of finalization (awaited)
            await SendNotificationsAsync(async () =>
            {
                try
                {
                    var requestingTeamId = (request.GetString("RequestingTeamId") ?? "").Trim();
                    var opponentTeamId = (request.GetString("OpponentTeamId") ?? "").Trim();
                    var proposedDate = request.GetString("ProposedGameDate") ?? "";
                    var proposedTime = request.GetString("ProposedStartTime") ?? "";
                    var proposedField = request.GetString("ProposedFieldName") ?? "";

                    var allCoaches = new List<(string userId, string team)>();
                    foreach (var coachId in await GetCoachesForTeamAsync(leagueId, request.GetString("Division") ?? "", requestingTeamId))
                        allCoaches.Add((coachId, requestingTeamId));
                    foreach (var coachId in await GetCoachesForTeamAsync(leagueId, request.GetString("Division") ?? "", opponentTeamId))
                        allCoaches.Add((coachId, opponentTeamId));

                    var tasks = allCoaches.Select(c =>
                        _notificationService.CreateNotificationAsync(
                            c.userId, leagueId, "RescheduleFinalized",
                            $"Game reschedule confirmed: {proposedDate} at {proposedTime} at {proposedField}.",
                            "#calendar", requestId, "GameReschedule"));
                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send reschedule finalization notifications for request {RequestId}", requestId);
                }
            });

            _logger.LogInformation("Game reschedule finalized: {RequestId}", requestId);

            return request;
        }
        catch (ApiGuards.HttpError) { throw; }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.CONFLICT,
                "The game or replacement changed while this request was being finalized. Refresh and review before retrying.");
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
        if (status != GameRescheduleRequestStatuses.PendingOpponent)
            throw new ApiGuards.HttpError(409, ErrorCodes.INVALID_STATUS_TRANSITION,
                "Only requests awaiting opponent approval can be cancelled. An approved move must finish or be recovered.");

        var requestingTeamId = (request.GetString("RequestingTeamId") ?? "").Trim();
        await EnsureRequestingTeamAuthorization(userId, leagueId, request.GetString("Division") ?? "", requestingTeamId);

        // Update request status
        request["Status"] = GameRescheduleRequestStatuses.Cancelled;
        request["UpdatedUtc"] = _clock.GetUtcNow();

        await _requestRepo.UpdateRequestAsync(request, request.ETag);

        // Notify opponent team of cancellation (awaited)
        await SendNotificationsAsync(async () =>
        {
            try
            {
                var opponentTeamId = (request.GetString("OpponentTeamId") ?? "").Trim();
                var coaches = await GetCoachesForTeamAsync(leagueId, request.GetString("Division") ?? "", opponentTeamId);
                var originalDate = request.GetString("OriginalGameDate") ?? "";
                var originalTime = request.GetString("OriginalStartTime") ?? "";

                var tasks = coaches.Select(coachUserId =>
                    _notificationService.CreateNotificationAsync(
                        coachUserId, leagueId, "RescheduleCancelled",
                        $"Reschedule request for the game on {originalDate} at {originalTime} has been cancelled by the requesting team.",
                        "#calendar", requestId, "GameReschedule"));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reschedule cancellation notification for request {RequestId}", requestId);
            }
        });

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
        var awayTeamId = SlotEntityUtil.ReadOpponentTeamId(originalSlot);
        var proposedDate = (proposedSlot.GetString("GameDate") ?? "").Trim();
        var proposedStartTime = (proposedSlot.GetString("StartTime") ?? "").Trim();
        var proposedEndTime = (proposedSlot.GetString("EndTime") ?? "").Trim();

        if (!TimeUtil.TryParseMinutes(proposedStartTime, out var proposedStartMin) ||
            !TimeUtil.TryParseMinutes(proposedEndTime, out var proposedEndMin))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME_RANGE, "The replacement slot has an invalid time range.");
        }

        // Check conflicts for both teams
        var homeTeamConflicts = await FindTeamConflicts(leagueId, division, homeTeamId, proposedDate, proposedStartMin, proposedEndMin, proposedSlotId, originalSlotId);
        var awayTeamConflicts = await FindTeamConflicts(leagueId, division, awayTeamId, proposedDate, proposedStartMin, proposedEndMin, proposedSlotId, originalSlotId);

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
        string excludeSlotId,
        string originalSlotId)
    {
        var conflicts = new List<GameRescheduleConflictDto>();

        // Query all confirmed slots for this team on the proposed date
        var slotsOnDate = await _slotRepo.QueryAllSlotsAsync(new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            FromDate = proposedDate,
            ToDate = proposedDate,
            Statuses = new List<string> { Constants.Status.SlotOpen, Constants.Status.SlotConfirmed },
            ExcludeAvailability = true,
            PageSize = 100
        });

        foreach (var slot in slotsOnDate)
        {
            // Skip the proposed slot itself
            if (string.Equals(slot.RowKey, excludeSlotId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(slot.RowKey, originalSlotId, StringComparison.OrdinalIgnoreCase))
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

    private static Task SendNotificationsAsync(Func<Task> send) => send();

    private void ValidateLeadTime(TableEntity slot, int minimumHours)
        => ScheduleTime.RequireLeadTime(slot.GetString("GameDate"), slot.GetString("StartTime"), minimumHours, _clock.GetUtcNow());

    private async Task EnsureOpponentAuthorization(string userId, string leagueId, string division, string opponentTeamId)
    {
        var isAdmin = await _membershipRepo.IsGlobalAdminAsync(userId);
        if (isAdmin) return;

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? "").Trim();

        if (string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            return;

        var userTeamId = (membership?.GetString("TeamId") ?? membership?.GetString("CoachTeamId") ?? "").Trim();

        if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(userTeamId, opponentTeamId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(membership?.GetString("Division"), division, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                "Only the opponent team coach can approve or reject this request.");
        }
    }

    private async Task EnsureRequestingTeamAuthorization(string userId, string leagueId, string division, string requestingTeamId)
    {
        var isAdmin = await _membershipRepo.IsGlobalAdminAsync(userId);
        if (isAdmin) return;

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? "").Trim();

        if (string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            return;

        var userTeamId = (membership?.GetString("TeamId") ?? membership?.GetString("CoachTeamId") ?? "").Trim();

        if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(userTeamId, requestingTeamId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(membership?.GetString("Division"), division, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
                "Only the requesting team coach can cancel this request.");
        }
    }

    private async Task<List<string>> GetCoachesForTeamAsync(string leagueId, string division, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return new List<string>();

        var memberships = await _membershipRepo.GetLeagueMembershipsAsync(leagueId);
        return memberships
            .Where(m =>
            {
                var role = (m.GetString("Role") ?? "").Trim();
                var coachTeamId = (m.GetString("TeamId") ?? m.GetString("CoachTeamId") ?? "").Trim();
                return string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(coachTeamId, teamId, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(m.GetString("Division"), division, StringComparison.OrdinalIgnoreCase);
            })
            .Select(m => m.PartitionKey)
            .Distinct()
            .ToList();
    }
}
