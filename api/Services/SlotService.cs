using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of ISlotService for slot business logic.
/// </summary>
public class SlotService : ISlotService
{
    private readonly record struct CoachRecipient(string UserId, string Email);

    private readonly ISlotRepository _slotRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly IAuthorizationService _authService;
    private readonly INotificationService _notificationService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly INotificationPreferencesService _preferencesService;
    private readonly IEmailService _emailService;
    private readonly IGameUmpireAssignmentRepository _umpireAssignmentRepo;
    private readonly ILogger<SlotService> _logger;

    public SlotService(
        ISlotRepository slotRepo,
        IFieldRepository fieldRepo,
        IAuthorizationService authService,
        INotificationService notificationService,
        IMembershipRepository membershipRepo,
        INotificationPreferencesService preferencesService,
        IEmailService emailService,
        IGameUmpireAssignmentRepository umpireAssignmentRepo,
        ILogger<SlotService> logger)
    {
        _slotRepo = slotRepo;
        _fieldRepo = fieldRepo;
        _authService = authService;
        _notificationService = notificationService;
        _membershipRepo = membershipRepo;
        _preferencesService = preferencesService;
        _emailService = emailService;
        _umpireAssignmentRepo = umpireAssignmentRepo;
        _logger = logger;
    }

    public async Task<object> CreateSlotAsync(CreateSlotRequest request, CorrelationContext context)
    {
        _logger.LogInformation("Creating slot for league {LeagueId}, division {Division}, correlation {CorrelationId}",
            context.LeagueId, request.Division, context.CorrelationId);

        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Division) ||
            string.IsNullOrWhiteSpace(request.OfferingTeamId) ||
            string.IsNullOrWhiteSpace(request.GameDate) ||
            string.IsNullOrWhiteSpace(request.StartTime) ||
            string.IsNullOrWhiteSpace(request.EndTime) ||
            string.IsNullOrWhiteSpace(request.FieldKey))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD,
                "division, offeringTeamId, gameDate, startTime, endTime, and fieldKey are required");
        }

        // Validate date format
        if (!DateTimeUtil.IsValidDate(request.GameDate))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_DATE,
                "gameDate must be in YYYY-MM-DD format");
        }

        // Validate time range
        if (!TimeUtil.IsValidRange(request.StartTime, request.EndTime, out var startMin, out var endMin, out var timeErr))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_TIME_RANGE, timeErr);
        }

        // Authorization check
        if (!await _authService.CanCreateSlotAsync(context.UserId, context.LeagueId, request.Division, request.OfferingTeamId))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                "Not authorized to create slot for this division/team");
        }

        // Parse and validate field key
        if (!FieldKeyUtil.TryParseFieldKey(request.FieldKey, out var parkCode, out var fieldCode))
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.INVALID_FIELD_KEY,
                "fieldKey must be in format parkCode/fieldCode");
        }

        // Validate field exists and is active
        var field = await _fieldRepo.GetFieldAsync(context.LeagueId, parkCode, fieldCode);
        if (field == null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.FIELD_NOT_FOUND,
                $"Field not found: {parkCode}/{fieldCode}");
        }

        var isActive = field.GetBoolean("IsActive") ?? true;
        if (!isActive)
        {
            throw new ApiGuards.HttpError(400, ErrorCodes.FIELD_INACTIVE,
                "Field is not active and cannot be used for new slots");
        }

        // Check for slot conflicts
        var normalizedFieldKey = FieldKeyUtil.NormalizeFieldKey(parkCode, fieldCode);
        if (await _slotRepo.HasConflictAsync(context.LeagueId, normalizedFieldKey, request.GameDate, startMin, endMin))
        {
            throw new ApiGuards.HttpError(409, ErrorCodes.SLOT_CONFLICT,
                "Field already has a slot at the requested time");
        }

        // Get field display information
        var normalizedParkName = field.GetString("ParkName") ?? request.ParkName ?? "";
        var normalizedFieldName = field.GetString("FieldName") ?? request.FieldName ?? "";
        var displayName = field.GetString("DisplayName") ?? $"{normalizedParkName} > {normalizedFieldName}";

        // Create slot entity
        var slotId = Guid.NewGuid().ToString("N");
        var pk = Constants.Pk.Slots(context.LeagueId, request.Division);
        var now = DateTimeOffset.UtcNow;

        var entity = new TableEntity(pk, slotId)
        {
            ["LeagueId"] = context.LeagueId,
            ["SlotId"] = slotId,
            ["Division"] = request.Division,
            ["OfferingTeamId"] = request.OfferingTeamId,
            ["HomeTeamId"] = request.OfferingTeamId,
            ["AwayTeamId"] = "",
            ["IsExternalOffer"] = false,
            ["IsAvailability"] = false,
            ["OfferingEmail"] = request.OfferingEmail ?? context.UserEmail ?? "",
            ["GameDate"] = request.GameDate,
            ["ParkName"] = normalizedParkName,
            ["FieldName"] = normalizedFieldName,
            ["DisplayName"] = displayName,
            ["FieldKey"] = normalizedFieldKey,
            ["GameType"] = request.GameType,
            ["Status"] = Constants.Status.SlotOpen,
            ["Notes"] = request.Notes ?? "",
            ["CreatedUtc"] = now,
            ["UpdatedUtc"] = now
        };

        SlotEntityUtil.ApplyTimeRange(entity, request.StartTime, request.EndTime);

        await _slotRepo.CreateSlotAsync(entity);

        // RACE CONDITION MITIGATION: Verify no conflicts after creation
        // This catches cases where concurrent requests both passed the pre-check
        var postCreateConflict = await _slotRepo.HasConflictAsync(
            context.LeagueId,
            normalizedFieldKey,
            request.GameDate,
            startMin,
            endMin,
            excludeSlotId: slotId); // Exclude the slot we just created

        if (postCreateConflict)
        {
            // Conflict detected - another request created a conflicting slot concurrently
            // Delete our slot and throw conflict error
            try
            {
                await _slotRepo.DeleteSlotAsync(context.LeagueId, request.Division, slotId);
                _logger.LogWarning("Deleted slot {SlotId} due to concurrent conflict on {FieldKey} at {GameDate} {StartMin}-{EndMin}",
                    slotId, normalizedFieldKey, request.GameDate, startMin, endMin);
            }
            catch (Exception deleteEx)
            {
                _logger.LogError(deleteEx, "Failed to delete conflicting slot {SlotId} during race condition cleanup", slotId);
            }

            throw new ApiGuards.HttpError(409, ErrorCodes.SLOT_CONFLICT,
                "Field already has a slot at the requested time");
        }

        _logger.LogInformation("Slot created successfully: {SlotId}", slotId);

        // Send batch notifications to all coaches in division (fire and forget - don't block response)
        _ = Task.Run(async () =>
        {
            try
            {
                // Get all coaches in this division
                var allCoaches = await GetCoachesInDivisionAsync(context.LeagueId, request.Division);

                var notificationTasks = new List<Task>();

                foreach (var coach in allCoaches)
                {
                    var coachUserId = coach.userId;
                    var coachEmail = coach.email;

                    // Skip the offering coach (they know they created it)
                    if (coachUserId == context.UserId)
                        continue;

                    // Create in-app notification
                    var message = $"New game slot available: {request.GameDate} at {request.StartTime} at {displayName}";
                    notificationTasks.Add(_notificationService.CreateNotificationAsync(
                        coachUserId,
                        context.LeagueId,
                        "SlotCreated",
                        message,
                        "#calendar",
                        slotId,
                        "Slot"));

                    // Send email if enabled
                    if (!string.IsNullOrWhiteSpace(coachEmail) &&
                        await _preferencesService.ShouldSendEmailAsync(coachUserId, context.LeagueId, "SlotCreated"))
                    {
                        notificationTasks.Add(_emailService.SendSlotCreatedEmailAsync(
                            coachEmail,
                            context.LeagueId,
                            request.Division,
                            request.GameDate,
                            request.StartTime,
                            displayName));
                    }
                }

                // Wait for all notifications to be sent
                await Task.WhenAll(notificationTasks);

                _logger.LogInformation("Sent {Count} notifications for slot creation: {SlotId}", allCoaches.Count - 1, slotId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send batch notifications for slot creation: {SlotId}", slotId);
            }
        });

        // Return mapped response
        return EntityMappers.MapSlot(entity);
    }

    public async Task<object?> GetSlotAsync(string leagueId, string division, string slotId)
    {
        var entity = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
        if (entity == null) return null;

        return EntityMappers.MapSlot(entity);
    }

    public async Task<SlotQueryResponse> QuerySlotsAsync(SlotQueryRequest request, CorrelationContext context)
    {
        var statusList = ParseStatusList(request.Status);
        var fieldKeyFilter = (request.FieldKey ?? "").Trim();
        var fromDateNorm = NormalizeIsoDate(request.FromDate);
        var toDateNorm = NormalizeIsoDate(request.ToDate);

        var filter = new SlotQueryFilter
        {
            LeagueId = request.LeagueId,
            Division = request.Division,
            Statuses = statusList,
            ExcludeCancelled = statusList.Count == 0,
            ExcludeAvailability = request.ExcludeAvailability,
            FromDate = fromDateNorm,
            ToDate = toDateNorm,
            FieldKey = fieldKeyFilter,
            PageSize = request.PageSize
        };

        var result = await _slotRepo.QuerySlotsAsync(filter, request.ContinuationToken);

        var sortedItems = result.Items
            .OrderBy(e => (e.GetString("GameDate") ?? "").Trim())
            .ThenBy(e => (e.GetString("StartTime") ?? "").Trim())
            .ThenBy(e => (e.GetString("DisplayName") ?? "").Trim())
            .ToList();

        return new SlotQueryResponse
        {
            Items = sortedItems.Select(EntityMappers.MapSlot).ToList(),
            ContinuationToken = result.ContinuationToken,
            PageSize = result.PageSize,
            HasMore = result.HasMore
        };
    }

    private static List<string> ParseStatusList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeIsoDate(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return "";
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public async Task CancelSlotAsync(string leagueId, string division, string slotId, string userId)
    {
        // Get the slot first to check team ownership
        var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
        if (slot == null)
        {
            throw new ApiGuards.HttpError(404, ErrorCodes.SLOT_NOT_FOUND,
                "Slot not found");
        }

        // Check if already cancelled
        var status = (slot.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
        if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Slot already cancelled: {LeagueId}/{Division}/{SlotId}", leagueId, division, slotId);
            return; // Already cancelled, return success
        }

        // Extract team IDs for authorization
        var offeringTeamId = (slot.GetString("OfferingTeamId") ?? "").Trim();
        var confirmedTeamId = (slot.GetString("ConfirmedTeamId") ?? "").Trim();

        // Authorization check with team ownership
        if (!await _authService.CanCancelSlotAsync(userId, leagueId, offeringTeamId, confirmedTeamId))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                "Not authorized to cancel this slot");
        }

        await _slotRepo.CancelSlotAsync(leagueId, division, slotId);

        _logger.LogInformation("Slot cancelled: {LeagueId}/{Division}/{SlotId} by user {UserId}",
            leagueId, division, slotId, userId);

        // Cancel umpire assignments (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await PropagateGameCancellationToUmpireAssignmentsAsync(leagueId, division, slotId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to propagate game cancellation to umpire assignments for slot {SlotId}", slotId);
            }
        });

        // Send notification (fire and forget - don't block response)
        _ = Task.Run(async () =>
        {
            try
            {
                var gameDate = slot.GetString("GameDate") ?? "";
                var startTime = slot.GetString("StartTime") ?? "";
                var fieldName = (slot.GetString("DisplayName") ?? slot.GetString("FieldKey") ?? "Field TBD").Trim();
                var recipientsByTeam = await GetCoachRecipientsByTeamAsync(
                    leagueId,
                    division,
                    new[] { offeringTeamId, confirmedTeamId });

                var notificationTasks = new List<Task>();
                var reason = "Cancelled in SportsScheduler.";

                foreach (var recipients in recipientsByTeam.Values)
                {
                    foreach (var recipient in recipients)
                    {
                        notificationTasks.Add(NotifyCancelledCoachAsync(
                            recipient,
                            leagueId,
                            slotId,
                            gameDate,
                            startTime,
                            fieldName,
                            reason));
                    }
                }

                await Task.WhenAll(notificationTasks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create notification for slot cancellation: {SlotId}", slotId);
            }
        });
    }

    private async Task<List<(string userId, string email)>> GetCoachesInDivisionAsync(string leagueId, string division)
    {
        try
        {
            // Query all memberships for this league
            var allMemberships = await _membershipRepo.GetLeagueMembershipsAsync(leagueId);

            var coaches = allMemberships
                .Where(m =>
                {
                    var role = (m.GetString("Role") ?? "").Trim();
                    var coachDivision = ReadMembershipDivision(m);

                    return string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(coachDivision, division, StringComparison.OrdinalIgnoreCase);
                })
                .Select(m => (
                    userId: m.PartitionKey,
                    email: m.GetString("Email") ?? ""
                ))
                .ToList();

            return coaches;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get coaches for division {Division} in league {LeagueId}", division, leagueId);
            return new List<(string, string)>();
        }
    }

    private async Task NotifyCancelledCoachAsync(
        CoachRecipient recipient,
        string leagueId,
        string slotId,
        string gameDate,
        string startTime,
        string fieldName,
        string reason)
    {
        var message = $"Game cancelled: {gameDate} at {startTime} on {fieldName}.";
        await _notificationService.CreateNotificationAsync(
            recipient.UserId,
            leagueId,
            "SlotCancelled",
            message,
            "#calendar",
            slotId,
            "Slot");

        if (!string.IsNullOrWhiteSpace(recipient.Email) &&
            await _preferencesService.ShouldSendEmailAsync(recipient.UserId, leagueId, "SlotCancelled"))
        {
            await _emailService.SendGameCancelledEmailAsync(
                recipient.Email,
                leagueId,
                gameDate,
                startTime,
                fieldName,
                reason);
        }
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

    /// <summary>
    /// Propagates game cancellation to umpire assignments.
    /// Called when a game is cancelled - cancels all umpire assignments and notifies umpires.
    /// </summary>
    private async Task PropagateGameCancellationToUmpireAssignmentsAsync(string leagueId, string division, string slotId)
    {
        try
        {
            var assignments = await _umpireAssignmentRepo.GetAssignmentsByGameAsync(leagueId, division, slotId);

            foreach (var assignment in assignments)
            {
                var status = (assignment.GetString("Status") ?? "").Trim();
                if (status == "Cancelled") continue;  // Already cancelled

                var umpireUserId = assignment.GetString("UmpireUserId") ?? "";
                var gameDesc = $"{assignment.GetString("HomeTeamId")} vs {assignment.GetString("AwayTeamId")} on {assignment.GetString("GameDate")} at {assignment.GetString("StartTime")}";

                // Update assignment status to Cancelled
                assignment["Status"] = "Cancelled";
                assignment["DeclineReason"] = "Game cancelled by league";
                assignment["UpdatedUtc"] = DateTime.UtcNow;

                await _umpireAssignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);

                // Notify umpire
                await _notificationService.CreateNotificationAsync(
                    umpireUserId,
                    leagueId,
                    "GameCancelled",
                    $"Game cancelled: {gameDesc}. Your assignment has been removed.",
                    "#umpire",
                    assignment.RowKey,
                    "UmpireAssignment");

                _logger.LogInformation("Cancelled umpire assignment {AssignmentId} due to game cancellation", assignment.RowKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to propagate game cancellation to umpire assignments for {SlotId}", slotId);
        }
    }
}
