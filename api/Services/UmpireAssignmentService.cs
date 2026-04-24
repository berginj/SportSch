using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of IUmpireAssignmentService for umpire assignment management.
/// CRITICAL: Includes conflict detection to prevent umpire double-booking.
/// </summary>
public class UmpireAssignmentService : IUmpireAssignmentService
{
    private readonly IGameUmpireAssignmentRepository _assignmentRepo;
    private readonly IUmpireProfileRepository _umpireRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly INotificationService _notificationService;
    private readonly UmpireNotificationService _umpireNotificationService;
    private readonly ILogger<UmpireAssignmentService> _logger;

    public UmpireAssignmentService(
        IGameUmpireAssignmentRepository assignmentRepo,
        IUmpireProfileRepository umpireRepo,
        ISlotRepository slotRepo,
        IMembershipRepository membershipRepo,
        INotificationService notificationService,
        UmpireNotificationService umpireNotificationService,
        ILogger<UmpireAssignmentService> logger)
    {
        _assignmentRepo = assignmentRepo;
        _umpireRepo = umpireRepo;
        _slotRepo = slotRepo;
        _membershipRepo = membershipRepo;
        _notificationService = notificationService;
        _umpireNotificationService = umpireNotificationService;
        _logger = logger;
    }

    public async Task<object> AssignUmpireToGameAsync(AssignUmpireRequest request, CorrelationContext context)
    {
        // 1. Validate umpire exists and is active
        var umpire = await _umpireRepo.GetUmpireAsync(request.LeagueId, request.UmpireUserId);
        if (umpire == null)
            throw new ApiGuards.HttpError(404, ErrorCodes.UMPIRE_NOT_FOUND, "Umpire not found");

        var isActive = umpire.GetBoolean("IsActive") ?? false;
        if (!isActive)
            throw new ApiGuards.HttpError(400, ErrorCodes.UMPIRE_INACTIVE,
                "This umpire is inactive and cannot be assigned to games");

        // 2. Validate game exists
        var game = await _slotRepo.GetSlotAsync(request.LeagueId, request.Division, request.SlotId);
        if (game == null)
            throw new ApiGuards.HttpError(404, ErrorCodes.SLOT_NOT_FOUND, "Game not found");

        // 3. Check if umpire already assigned to this game (prevent duplicate assignments)
        var existingAssignment = await _assignmentRepo.GetAssignmentByGameAndUmpireAsync(
            request.LeagueId, request.Division, request.SlotId, request.UmpireUserId);

        if (existingAssignment != null)
        {
            var existingStatus = (existingAssignment.GetString("Status") ?? "").Trim();
            if (existingStatus != "Declined" && existingStatus != "Cancelled")
            {
                throw new ApiGuards.HttpError(409, ErrorCodes.ALREADY_ASSIGNED,
                    "This umpire is already assigned to this game");
            }
        }

        // 4. Extract game details
        var gameDate = game.GetString("GameDate") ?? "";
        var startTime = game.GetString("StartTime") ?? "";
        var endTime = game.GetString("EndTime") ?? "";
        var startMin = game.GetInt32("StartMin") ?? 0;
        var endMin = game.GetInt32("EndMin") ?? 0;

        // Parse times if not stored as minutes
        if (startMin == 0 && !string.IsNullOrWhiteSpace(startTime))
        {
            if (!TimeUtil.TryParseMinutes(startTime, out startMin))
                throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME, "Invalid start time format");
        }

        if (endMin == 0 && !string.IsNullOrWhiteSpace(endTime))
        {
            if (!TimeUtil.TryParseMinutes(endTime, out endMin))
                throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME, "Invalid end time format");
        }

        // 5. CRITICAL: Check for umpire conflicts (double-booking prevention)
        var conflicts = await CheckUmpireConflictsAsync(
            request.LeagueId,
            request.UmpireUserId,
            gameDate,
            startMin,
            endMin,
            request.SlotId);

        if (conflicts.Any())
        {
            var conflict = conflicts[0];
            throw new ApiGuards.HttpError(409, ErrorCodes.UMPIRE_CONFLICT,
                $"Umpire has conflicting assignment from {conflict.GetType().GetProperty("startTime")?.GetValue(conflict)} to {conflict.GetType().GetProperty("endTime")?.GetValue(conflict)} at {conflict.GetType().GetProperty("field")?.GetValue(conflict)}");
        }

        // 6. Create assignment
        var assignmentId = Guid.NewGuid().ToString("N");
        var pk = $"UMPASSIGN|{request.LeagueId}|{request.Division}|{request.SlotId}";
        var now = DateTime.UtcNow;

        var assignment = new TableEntity(pk, assignmentId)
        {
            ["LeagueId"] = request.LeagueId,
            ["Division"] = request.Division,
            ["SlotId"] = request.SlotId,
            ["AssignmentId"] = assignmentId,
            ["UmpireUserId"] = request.UmpireUserId,
            ["Position"] = request.Position ?? "",
            ["Status"] = "Assigned",
            ["AssignedBy"] = context.UserId,
            ["AssignedUtc"] = DateTimeOffset.UtcNow,

            // Denormalized game details (for fast umpire queries)
            ["GameDate"] = gameDate,
            ["StartTime"] = startTime,
            ["EndTime"] = endTime,
            ["StartMin"] = startMin,
            ["EndMin"] = endMin,
            ["FieldKey"] = game.GetString("FieldKey") ?? "",
            ["FieldDisplayName"] = game.GetString("DisplayName") ?? game.GetString("FieldName") ?? "",
            ["HomeTeamId"] = game.GetString("HomeTeamId") ?? "",
            ["AwayTeamId"] = game.GetString("AwayTeamId") ?? "",

            ["CreatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        await _assignmentRepo.CreateAssignmentAsync(assignment);

        _logger.LogInformation("Assigned umpire {UmpireUserId} to game {SlotId}", request.UmpireUserId, request.SlotId);

        // 7. Send notification (fire-and-forget with email)
        if (request.SendNotification)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _umpireNotificationService.SendAssignmentNotificationAsync(
                        request.UmpireUserId,
                        request.LeagueId,
                        assignment);

                    _logger.LogInformation("Sent assignment notification (in-app + email) to umpire {UmpireUserId} for assignment {AssignmentId}",
                        request.UmpireUserId, assignmentId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send umpire assignment notification for {AssignmentId}", assignmentId);
                }
            });
        }

        return MapAssignmentToDto(assignment, umpire);
    }

    public async Task<List<object>> CheckUmpireConflictsAsync(
        string leagueId,
        string umpireUserId,
        string gameDate,
        int startMin,
        int endMin,
        string? excludeSlotId = null)
    {
        // Get all assignments for this umpire on the same date
        var assignments = await _assignmentRepo.GetAssignmentsByUmpireAndDateAsync(
            leagueId,
            umpireUserId,
            gameDate);

        var conflicts = new List<object>();

        foreach (var assignment in assignments)
        {
            // Skip the game we're checking against (for reassignment scenarios)
            var slotId = assignment.GetString("SlotId") ?? "";
            if (!string.IsNullOrWhiteSpace(excludeSlotId) &&
                string.Equals(slotId, excludeSlotId, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip cancelled or declined assignments
            var status = (assignment.GetString("Status") ?? "").Trim();
            if (status == "Cancelled" || status == "Declined")
                continue;

            // Check time overlap (reuse existing TimeUtil logic)
            var assignmentStartMin = assignment.GetInt32("StartMin") ?? 0;
            var assignmentEndMin = assignment.GetInt32("EndMin") ?? 0;

            if (TimeUtil.Overlaps(startMin, endMin, assignmentStartMin, assignmentEndMin))
            {
                conflicts.Add(new
                {
                    slotId = assignment.GetString("SlotId"),
                    division = assignment.GetString("Division"),
                    gameDate = assignment.GetString("GameDate"),
                    startTime = assignment.GetString("StartTime"),
                    endTime = assignment.GetString("EndTime"),
                    field = assignment.GetString("FieldDisplayName") ?? assignment.GetString("FieldKey"),
                    homeTeam = assignment.GetString("HomeTeamId"),
                    awayTeam = assignment.GetString("AwayTeamId"),
                    assignmentStatus = assignment.GetString("Status")
                });
            }
        }

        return conflicts;
    }

    public async Task<object> UpdateAssignmentStatusAsync(
        string assignmentId,
        string newStatus,
        string? reason,
        CorrelationContext context)
    {
        // Get assignment
        var assignment = await _assignmentRepo.GetAssignmentAsync(context.LeagueId, assignmentId);
        if (assignment == null)
            throw new ApiGuards.HttpError(404, ErrorCodes.ASSIGNMENT_NOT_FOUND, "Assignment not found");

        var umpireUserId = assignment.GetString("UmpireUserId") ?? "";
        var currentStatus = (assignment.GetString("Status") ?? "").Trim();

        // Authorization: Umpire (self only) OR LeagueAdmin
        var isSelf = string.Equals(context.UserId, umpireUserId, StringComparison.OrdinalIgnoreCase);
        var isAdmin = await IsLeagueAdminAsync(context.UserId, context.LeagueId);

        // Umpires can only accept/decline their own assignments
        if (isSelf && (newStatus != "Accepted" && newStatus != "Declined"))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                "Umpires can only accept or decline assignments");
        }

        if (!isSelf && !isAdmin)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                "Only the assigned umpire or league admin can update this assignment");
        }

        // Validate status transition
        if (!IsValidStatusTransition(currentStatus, newStatus))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.INVALID_STATUS_TRANSITION,
                $"Invalid status transition from {currentStatus} to {newStatus}");
        }

        // Update assignment
        assignment["Status"] = newStatus;
        assignment["UpdatedUtc"] = DateTime.UtcNow;

        if (newStatus == "Accepted" || newStatus == "Declined")
        {
            assignment["ResponseUtc"] = DateTimeOffset.UtcNow;
        }

        if (newStatus == "Declined" && !string.IsNullOrWhiteSpace(reason))
        {
            assignment["DeclineReason"] = reason.Trim();
        }

        await _assignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);

        _logger.LogInformation("Updated assignment {AssignmentId} status from {Old} to {New}",
            assignmentId, currentStatus, newStatus);

        // Notify admin if umpire accepted or declined
        if (isSelf && (newStatus == "Accepted" || newStatus == "Declined"))
        {
            await NotifyAdminOfUmpireResponseAsync(assignment, newStatus, reason);
        }

        return MapAssignmentToDto(assignment);
    }

    public async Task RemoveAssignmentAsync(string assignmentId, CorrelationContext context)
    {
        // Authorization: LeagueAdmin only
        var isAdmin = await IsLeagueAdminAsync(context.UserId, context.LeagueId);
        if (!isAdmin)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                "Only league admins can remove umpire assignments");
        }

        var assignment = await _assignmentRepo.GetAssignmentAsync(context.LeagueId, assignmentId);
        if (assignment == null)
            throw new ApiGuards.HttpError(404, ErrorCodes.ASSIGNMENT_NOT_FOUND, "Assignment not found");

        var leagueId = assignment.GetString("LeagueId") ?? "";
        var division = assignment.GetString("Division") ?? "";
        var slotId = assignment.GetString("SlotId") ?? "";

        await _assignmentRepo.DeleteAssignmentAsync(leagueId, division, slotId, assignmentId);

        _logger.LogInformation("Removed umpire assignment {AssignmentId}", assignmentId);

        // Notify umpire of removal
        var umpireUserId = assignment.GetString("UmpireUserId") ?? "";
        var gameDesc = $"{assignment.GetString("HomeTeamId")} vs {assignment.GetString("AwayTeamId")} on {assignment.GetString("GameDate")}";

        await _notificationService.CreateNotificationAsync(
            umpireUserId,
            leagueId,
            "AssignmentRemoved",
            $"You have been unassigned from {gameDesc}",
            "#umpire",
            assignmentId,
            "UmpireAssignment");
    }

    public async Task<List<object>> GetUmpireAssignmentsAsync(string umpireUserId, AssignmentQueryFilter filter)
    {
        filter.UmpireUserId = umpireUserId;
        var assignments = await _assignmentRepo.QueryAssignmentsAsync(filter);

        return assignments.Select(a => MapAssignmentToDto(a, null)).ToList();
    }

    public async Task<List<object>> GetGameAssignmentsAsync(string leagueId, string division, string slotId)
    {
        var assignments = await _assignmentRepo.GetAssignmentsByGameAsync(leagueId, division, slotId);
        return assignments.Select(a => MapAssignmentToDto(a, null)).ToList();
    }

    public async Task<List<object>> GetUnassignedGamesAsync(string leagueId, UnassignedGamesFilter filter)
    {
        // Query all games in league with optional date/division filters
        var slotFilter = new SlotQueryFilter
        {
            LeagueId = leagueId,
            Division = filter.Division,
            FromDate = filter.DateFrom,
            ToDate = filter.DateTo,
            PageSize = filter.PageSize
        };

        var slots = await _slotRepo.QuerySlotsAsync(slotFilter, null);
        var unassignedGames = new List<object>();

        foreach (var slot in slots.Items)
        {
            var slotId = slot.RowKey;
            var division = slot.GetString("Division") ?? "";

            // Get assignments for this game
            var assignments = await _assignmentRepo.GetAssignmentsByGameAsync(leagueId, division, slotId);

            // Game is unassigned if:
            // - No assignments exist
            // - All assignments are Declined or Cancelled
            var hasActiveAssignment = assignments.Any(a =>
            {
                var status = (a.GetString("Status") ?? "").Trim();
                return status != "Declined" && status != "Cancelled";
            });

            if (!hasActiveAssignment)
            {
                unassignedGames.Add(new
                {
                    slotId = slot.RowKey,
                    division,
                    gameDate = slot.GetString("GameDate"),
                    startTime = slot.GetString("StartTime"),
                    endTime = slot.GetString("EndTime"),
                    fieldKey = slot.GetString("FieldKey"),
                    fieldDisplayName = slot.GetString("DisplayName") ?? slot.GetString("FieldName"),
                    homeTeamId = slot.GetString("HomeTeamId"),
                    awayTeamId = slot.GetString("AwayTeamId"),
                    status = slot.GetString("Status")
                });
            }
        }

        // Sort by date ascending (soonest games first)
        // Return as-is (client can sort if needed, or we can sort by extracting fields)
        return unassignedGames;
    }

    public async Task FlagNoShowAsync(string assignmentId, string notes, CorrelationContext context)
    {
        // Authorization: LeagueAdmin only
        var isAdmin = await IsLeagueAdminAsync(context.UserId, context.LeagueId);
        if (!isAdmin)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                "Only league admins can flag no-shows");
        }

        var assignment = await _assignmentRepo.GetAssignmentAsync(context.LeagueId, assignmentId);
        if (assignment == null)
            throw new ApiGuards.HttpError(404, ErrorCodes.ASSIGNMENT_NOT_FOUND, "Assignment not found");

        assignment["NoShowFlagged"] = true;
        assignment["NoShowNotes"] = notes?.Trim() ?? "";
        assignment["NoShowFlaggedUtc"] = DateTimeOffset.UtcNow;
        assignment["UpdatedUtc"] = DateTime.UtcNow;

        await _assignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);

        _logger.LogWarning("No-show flagged for umpire {UmpireUserId} on assignment {AssignmentId}",
            assignment.GetString("UmpireUserId"), assignmentId);
    }

    // ============================================
    // HELPER METHODS
    // ============================================

    private async Task<bool> IsLeagueAdminAsync(string userId, string leagueId)
    {
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
            return true;

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        var role = (membership?.GetString("Role") ?? "").Trim();
        return string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidStatusTransition(string from, string to)
    {
        // Idempotent
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return true;

        return (from.ToLower(), to.ToLower()) switch
        {
            ("assigned", "accepted") => true,
            ("assigned", "declined") => true,
            ("assigned", "cancelled") => true,
            ("accepted", "cancelled") => true,  // Admin can cancel after umpire accepted
            ("declined", "assigned") => true,   // Admin can reassign after decline
            _ => false
        };
    }

    private async Task NotifyAdminOfUmpireResponseAsync(TableEntity assignment, string newStatus, string? reason)
    {
        var leagueId = assignment.GetString("LeagueId") ?? "";
        var gameDesc = $"{assignment.GetString("HomeTeamId")} vs {assignment.GetString("AwayTeamId")} on {assignment.GetString("GameDate")}";

        var message = newStatus == "Accepted"
            ? $"Umpire accepted assignment for {gameDesc}"
            : $"Umpire declined assignment for {gameDesc}. Reason: {reason ?? "No reason provided"}";

        // Notify all league admins
        var admins = await _membershipRepo.GetLeagueMembershipsAsync(leagueId);
        var adminUserIds = admins
            .Where(m => string.Equals(m.GetString("Role"), Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.PartitionKey)
            .Distinct()
            .ToList();

        foreach (var adminUserId in adminUserIds)
        {
            await _notificationService.CreateNotificationAsync(
                adminUserId,
                leagueId,
                newStatus == "Accepted" ? "UmpireAccepted" : "UmpireDeclined",
                message,
                "#admin",
                assignment.GetString("AssignmentId"),
                "UmpireAssignment");
        }
    }

    private static object MapAssignmentToDto(TableEntity assignment, TableEntity? umpire = null)
    {
        return new
        {
            assignmentId = assignment.RowKey,
            leagueId = assignment.GetString("LeagueId"),
            division = assignment.GetString("Division"),
            slotId = assignment.GetString("SlotId"),
            umpireUserId = assignment.GetString("UmpireUserId"),
            umpireName = umpire?.GetString("Name"),
            position = assignment.GetString("Position"),
            status = assignment.GetString("Status"),
            gameDate = assignment.GetString("GameDate"),
            startTime = assignment.GetString("StartTime"),
            endTime = assignment.GetString("EndTime"),
            fieldKey = assignment.GetString("FieldKey"),
            fieldDisplayName = assignment.GetString("FieldDisplayName"),
            homeTeamId = assignment.GetString("HomeTeamId"),
            awayTeamId = assignment.GetString("AwayTeamId"),
            assignedBy = assignment.GetString("AssignedBy"),
            assignedUtc = assignment.GetDateTime("AssignedUtc"),
            responseUtc = assignment.GetDateTime("ResponseUtc"),
            declineReason = assignment.GetString("DeclineReason"),
            noShowFlagged = assignment.GetBoolean("NoShowFlagged") ?? false,
            noShowNotes = assignment.GetString("NoShowNotes")
        };
    }
}
