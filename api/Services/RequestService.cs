using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of IRequestService for slot request business logic.
/// </summary>
public class RequestService : IRequestService
{
    private readonly record struct CoachRecipient(string UserId, string Email);
    private readonly IRequestRepository _requestRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly INotificationService _notificationService;
    private readonly INotificationPreferencesService _preferencesService;
    private readonly IEmailService _emailService;
    private readonly ILogger<RequestService> _logger;
    public RequestService(
        IRequestRepository requestRepo,
        ISlotRepository slotRepo,
        IMembershipRepository membershipRepo,
        INotificationService notificationService,
        INotificationPreferencesService preferencesService,
        IEmailService emailService,
        ILogger<RequestService> logger)
    {
        _requestRepo = requestRepo;
        _slotRepo = slotRepo;
        _membershipRepo = membershipRepo;
        _notificationService = notificationService;
        _preferencesService = preferencesService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<object> CreateRequestAsync(CreateRequestRequest request, CorrelationContext context)
    {
        _logger.LogInformation("Creating request for slot {LeagueId}/{Division}/{SlotId}, correlation {CorrelationId}",
            request.LeagueId, request.Division, request.SlotId, context.CorrelationId);

        // Get membership for authorization and coach assignment
        var membership = await _membershipRepo.GetMembershipAsync(context.UserId, request.LeagueId);
        if (membership == null && !await _membershipRepo.IsGlobalAdminAsync(context.UserId))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN, "Access denied: no membership for this league");
        }

        var role = (membership?.GetString("Role") ?? Constants.Roles.Viewer).Trim();
        var isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
        var isGlobalAdmin = await _membershipRepo.IsGlobalAdminAsync(context.UserId);

        // Determine requesting team
        var myDivisionRaw = ReadMembershipDivision(membership);
        var myTeamIdRaw = ReadMembershipTeamId(membership);
        var myDivision = myDivisionRaw.Trim().ToUpperInvariant();
        var myTeamId = myTeamIdRaw.Trim();

        var canOverrideTeam = isGlobalAdmin || isLeagueAdmin;
        if (string.IsNullOrWhiteSpace(myTeamId))
        {
            if (!canOverrideTeam)
            {
                throw new ApiGuards.HttpError(400, ErrorCodes.COACH_TEAM_REQUIRED,
                    "Coach role requires an assigned team to accept a game request.");
            }

            if (string.IsNullOrWhiteSpace(request.RequestingTeamId))
            {
                throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD,
                    "Select a team to accept this game request.");
            }

            // Admin overriding team - validate team exists (simplified check)
            if (!string.IsNullOrWhiteSpace(request.RequestingDivision) &&
                !string.Equals(request.RequestingDivision, request.Division, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_INPUT,
                    "Requested division must match the slot's division.");
            }

            myTeamId = request.RequestingTeamId;
            myDivision = request.Division.ToUpperInvariant();
        }

        // Exact division match
        if (!string.IsNullOrWhiteSpace(myDivision) &&
            !string.Equals(myDivision, request.Division, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.DIVISION_MISMATCH,
                "You can only accept game requests in your exact division.");
        }

        // Load slot
        var slot = await _slotRepo.GetSlotAsync(request.LeagueId, request.Division, request.SlotId);
        if (slot == null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.SLOT_NOT_FOUND, "Slot not found.");
        }

        var slotStatus = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
        if (!string.Equals(slotStatus, Constants.Status.SlotOpen, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.SLOT_NOT_OPEN, $"Slot is not open (status: {slotStatus}).");
        }

        var awayTeamId = (slot.GetString("AwayTeamId") ?? "").Trim();
        var isExternalOffer = slot.GetBoolean("IsExternalOffer") ?? false;
        var isAvailability = slot.GetBoolean("IsAvailability") ?? false;
        if (isAvailability)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.SLOT_UNASSIGNED,
                "This slot is availability only and cannot be accepted yet.");
        }
        if (!string.IsNullOrWhiteSpace(awayTeamId) && !isExternalOffer)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.SLOT_ASSIGNED,
                "This slot is already assigned to a league matchup.");
        }

        // Prevent accepting your own slot
        var offeringTeamId = (slot.GetString("OfferingTeamId") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(offeringTeamId) &&
            string.Equals(offeringTeamId, myTeamId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_INPUT,
                "You cannot accept your own game request.");
        }

        // Validate slot time fields
        var gameDate = (slot.GetString("GameDate") ?? "").Trim();
        var startTime = (slot.GetString("StartTime") ?? "").Trim();
        var endTime = (slot.GetString("EndTime") ?? "").Trim();

        if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", out _))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE, "Slot has invalid GameDate.");
        }

        if (!TimeUtil.IsValidRange(startTime, endTime, out var startMin, out var endMin, out _))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME_RANGE, "Slot has invalid StartTime/EndTime.");
        }

        // Double-booking prevention
        var conflicts = new List<object>();

        if (!string.IsNullOrWhiteSpace(offeringTeamId))
        {
            var c = await FindTeamConflictAsync(request.LeagueId, offeringTeamId, gameDate, startMin, endMin, excludeSlotId: request.SlotId);
            if (c is not null) conflicts.Add(c);
        }

        var myConflict = await FindTeamConflictAsync(request.LeagueId, myTeamId, gameDate, startMin, endMin, excludeSlotId: request.SlotId);
        if (myConflict is not null) conflicts.Add(myConflict);

        if (conflicts.Count > 0)
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.DOUBLE_BOOKING,
                "This game overlaps an existing confirmed game for one of the teams.");
        }

        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.NewGuid().ToString("N");

        // CRITICAL: Update slot FIRST to ensure atomicity
        // If slot update fails (race condition), we don't create orphaned request
        try
        {
            slot["Status"] = Constants.Status.SlotConfirmed;
            slot["ConfirmedTeamId"] = myTeamId;
            slot["ConfirmedRequestId"] = requestId;
            slot["ConfirmedBy"] = context.UserEmail ?? "";
            slot["ConfirmedUtc"] = now;
            slot["UpdatedUtc"] = now;

            await _slotRepo.UpdateSlotAsync(slot, slot.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            // Slot update failed - another team won the race
            // No rollback needed since we haven't created the request yet
            throw new ApiGuards.HttpError(409, ErrorCodes.CONFLICT, "Slot was confirmed by another team.");
        }

        // Slot confirmed successfully - now create the approved request
        var pk = Constants.Pk.SlotRequests(request.LeagueId, request.Division, request.SlotId);
        var reqEntity = new TableEntity(pk, requestId)
        {
            ["LeagueId"] = request.LeagueId,
            ["Division"] = request.Division,
            ["SlotId"] = request.SlotId,
            ["RequestId"] = requestId,
            ["RequestingUserId"] = context.UserId,
            ["RequestingTeamId"] = myTeamId,
            ["RequestingEmail"] = context.UserEmail ?? "",
            ["Notes"] = request.Notes,
            ["Status"] = Constants.Status.SlotRequestApproved,
            ["ApprovedBy"] = context.UserEmail ?? "",
            ["ApprovedUtc"] = now,
            ["RequestedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        await _requestRepo.CreateRequestAsync(reqEntity);

        // Best-effort: reject other pending requests for this slot
        var pendingRequests = await _requestRepo.GetPendingRequestsForSlotAsync(request.LeagueId, request.Division, request.SlotId);
        foreach (var other in pendingRequests)
        {
            if (other.RowKey == requestId) continue;

            other["Status"] = Constants.Status.SlotRequestDenied;
            other["RejectedUtc"] = now;
            other["UpdatedUtc"] = now;

            try { await _requestRepo.UpdateRequestAsync(other, other.ETag); } catch { }
        }

        _logger.LogInformation("Request created and slot confirmed: {RequestId}, slot {SlotId}", requestId, request.SlotId);

        // Send notification (fire and forget - don't block response)
        _ = Task.Run(async () =>
        {
            try
            {
                var gameDate = (slot.GetString("GameDate") ?? "").Trim();
                var startTime = (slot.GetString("StartTime") ?? "").Trim();
                var fieldName = (slot.GetString("DisplayName") ?? slot.GetString("FieldKey") ?? "Field TBD").Trim();

                var recipientsByTeam = await GetCoachRecipientsByTeamAsync(
                    request.LeagueId,
                    request.Division,
                    new[] { myTeamId, offeringTeamId });

                if (recipientsByTeam.TryGetValue(myTeamId, out var requestingCoaches))
                {
                    var requestApprovedTasks = requestingCoaches.Select(async recipient =>
                    {
                        var message = $"Game confirmed: {gameDate} at {startTime} on {fieldName}.";
                        await _notificationService.CreateNotificationAsync(
                            recipient.UserId,
                            request.LeagueId,
                            "RequestApproved",
                            message,
                            "#calendar",
                            request.SlotId,
                            "Slot");

                        if (!string.IsNullOrWhiteSpace(recipient.Email) &&
                            await _preferencesService.ShouldSendEmailAsync(recipient.UserId, request.LeagueId, "RequestApproved"))
                        {
                            await _emailService.SendRequestApprovedEmailAsync(
                                recipient.Email,
                                request.LeagueId,
                                gameDate,
                                startTime,
                                fieldName);
                        }
                    });

                    await Task.WhenAll(requestApprovedTasks);
                }

                if (recipientsByTeam.TryGetValue(offeringTeamId, out var offeringCoaches))
                {
                    var requestReceivedTasks = offeringCoaches.Select(async recipient =>
                    {
                        var message = $"{myTeamId} accepted your open game for {gameDate} at {startTime}. The game is now confirmed.";
                        await _notificationService.CreateNotificationAsync(
                            recipient.UserId,
                            request.LeagueId,
                            "RequestReceived",
                            message,
                            "#calendar",
                            request.SlotId,
                            "Slot");

                        if (!string.IsNullOrWhiteSpace(recipient.Email) &&
                            await _preferencesService.ShouldSendEmailAsync(recipient.UserId, request.LeagueId, "RequestReceived"))
                        {
                            await _emailService.SendRequestReceivedEmailAsync(
                                recipient.Email,
                                request.LeagueId,
                                myTeamId,
                                gameDate,
                                startTime);
                        }
                    });

                    await Task.WhenAll(requestReceivedTasks);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create notification for request approval: {RequestId}", requestId);
            }
        });

        return new
        {
            requestId,
            requestingTeamId = myTeamId,
            status = Constants.Status.SlotRequestApproved,
            slotStatus = Constants.Status.SlotConfirmed,
            confirmedTeamId = myTeamId,
            requestedUtc = now
        };
    }

    public async Task<List<object>> QueryRequestsAsync(string leagueId, string division, string slotId)
    {
        _logger.LogInformation("Querying requests for slot {LeagueId}/{Division}/{SlotId}", leagueId, division, slotId);

        // Validate slot exists (cheap guardrail)
        var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
        if (slot == null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.SLOT_NOT_FOUND, "Slot not found");
        }

        var filter = new RequestQueryFilter
        {
            LeagueId = leagueId,
            Division = division,
            SlotId = slotId,
            PageSize = 100 // No pagination for single slot's requests
        };

        var result = await _requestRepo.QueryRequestsAsync(filter, null);

        var mapped = result.Items
            .Select(e => new
            {
                requestId = e.RowKey,
                requestingTeamId = e.GetString("RequestingTeamId") ?? "",
                requestingEmail = e.GetString("RequestingEmail") ?? "",
                notes = e.GetString("Notes") ?? "",
                status = e.GetString("Status") ?? Constants.Status.SlotRequestPending,
                requestedUtc = e.GetDateTimeOffset("RequestedUtc")
            })
            .OrderByDescending(x => x.requestedUtc ?? DateTimeOffset.MinValue)
            .ToList<object>();

        return mapped;
    }

    /// <summary>
    /// Finds conflicting confirmed slots for a team on a specific date/time.
    /// </summary>
    private async Task<object?> FindTeamConflictAsync(
        string leagueId,
        string teamId,
        string gameDate,
        int startMin,
        int endMin,
        string? excludeSlotId)
    {
        // Query all divisions for this league on the same date
        // Check BOTH Confirmed and Open slots to prevent rapid double-booking
        var filter = new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = null, // All divisions
            Statuses = new List<string> { Constants.Status.SlotConfirmed, Constants.Status.SlotOpen },
            FromDate = gameDate,
            ToDate = gameDate,
            PageSize = 100
        };

        var result = await _slotRepo.QuerySlotsAsync(filter, null);

        foreach (var e in result.Items)
        {
            var conflictSlotId = e.RowKey;
            if (!string.IsNullOrWhiteSpace(excludeSlotId) &&
                string.Equals(conflictSlotId, excludeSlotId, StringComparison.OrdinalIgnoreCase))
                continue;

            var offeringTeamId = (e.GetString("OfferingTeamId") ?? "").Trim();
            var confirmedTeamId = (e.GetString("ConfirmedTeamId") ?? "").Trim();
            var homeTeamId = (e.GetString("HomeTeamId") ?? "").Trim();
            var awayTeamId = (e.GetString("AwayTeamId") ?? "").Trim();

            // Check if team is involved in this slot
            var involvesTeam =
                string.Equals(offeringTeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(confirmedTeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(homeTeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(awayTeamId, teamId, StringComparison.OrdinalIgnoreCase);

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
                    division = e.GetString("Division") ?? "",
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

    private async Task<Dictionary<string, List<CoachRecipient>>> GetCoachRecipientsByTeamAsync(
        string leagueId,
        string division,
        IEnumerable<string> teamIds)
    {
        var teams = new HashSet<string>(
            teamIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (teams.Count == 0)
        {
            return new Dictionary<string, List<CoachRecipient>>(StringComparer.OrdinalIgnoreCase);
        }

        var memberships = await _membershipRepo.GetLeagueMembershipsAsync(leagueId);
        var recipientsByTeam = new Dictionary<string, List<CoachRecipient>>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var membership in memberships)
        {
            var role = (membership.GetString("Role") ?? "").Trim();
            if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var membershipDivision = ReadMembershipDivision(membership);
            if (!string.Equals(membershipDivision, division, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var membershipTeamId = ReadMembershipTeamId(membership);
            if (!teams.Contains(membershipTeamId))
            {
                continue;
            }

            var userId = (membership.PartitionKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                continue;
            }

            var recipientKey = $"{membershipTeamId}|{userId}";
            if (!seen.Add(recipientKey))
            {
                continue;
            }

            if (!recipientsByTeam.TryGetValue(membershipTeamId, out var recipients))
            {
                recipients = new List<CoachRecipient>();
                recipientsByTeam[membershipTeamId] = recipients;
            }

            recipients.Add(new CoachRecipient(userId, (membership.GetString("Email") ?? "").Trim()));
        }

        return recipientsByTeam;
    }

    private static string ReadMembershipDivision(TableEntity? membership)
    {
        return (membership?.GetString("Division") ?? "").Trim();
    }

    private static string ReadMembershipTeamId(TableEntity? membership)
    {
        return (membership?.GetString("TeamId") ?? "").Trim();
    }
}
