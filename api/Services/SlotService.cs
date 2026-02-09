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
    private readonly ISlotRepository _slotRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly IAuthorizationService _authService;
    private readonly INotificationService _notificationService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly INotificationPreferencesService _preferencesService;
    private readonly IEmailService _emailService;
    private readonly ILogger<SlotService> _logger;

    public SlotService(
        ISlotRepository slotRepo,
        IFieldRepository fieldRepo,
        IAuthorizationService authService,
        INotificationService notificationService,
        IMembershipRepository membershipRepo,
        INotificationPreferencesService preferencesService,
        IEmailService emailService,
        ILogger<SlotService> logger)
    {
        _slotRepo = slotRepo;
        _fieldRepo = fieldRepo;
        _authService = authService;
        _notificationService = notificationService;
        _membershipRepo = membershipRepo;
        _preferencesService = preferencesService;
        _emailService = emailService;
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
            throw new ApiGuards.HttpError(403, ErrorCodes.UNAUTHORIZED,
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
            throw new ApiGuards.HttpError(409, ErrorCodes.FIELD_NOT_FOUND,
                "Field exists but is inactive");
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
            ["StartTime"] = request.StartTime,
            ["EndTime"] = request.EndTime,
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

        await _slotRepo.CreateSlotAsync(entity);

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

    public async Task<object> QuerySlotsAsync(SlotQueryRequest request, CorrelationContext context)
    {
        // Parse multiple status values if provided
        var statusList = ParseStatusList(request.Status);
        var fieldKeyFilter = (request.FieldKey ?? "").Trim();
        var fromDateNorm = NormalizeIsoDate(request.FromDate);
        var toDateNorm = NormalizeIsoDate(request.ToDate);

        var filter = new SlotQueryFilter
        {
            LeagueId = request.LeagueId,
            Division = request.Division,
            Status = statusList.Count == 1 ? statusList[0] : null, // Single status can use OData filter
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FieldKey = request.FieldKey,
            PageSize = request.PageSize
        };

        var result = await _slotRepo.QuerySlotsAsync(filter, request.ContinuationToken);

        // Apply filtering in memory as a safety net for legacy mixed-typed rows.
        var filteredItems = result.Items.Where(e =>
        {
            var status = ReadString(e, "Status", Constants.Status.SlotOpen);
            if (statusList.Count > 0)
            {
                if (!statusList.Contains(status, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
            else if (string.IsNullOrWhiteSpace(request.Status))
            {
                if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(fieldKeyFilter))
            {
                var fieldKey = ReadString(e, "FieldKey");
                if (!string.Equals(fieldKey, fieldKeyFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            var gameDate = ReadString(e, "GameDate");
            if (!IsWithinDateRange(gameDate, fromDateNorm, toDateNorm))
                return false;

            return true;
        }).ToList();

        // Sort by date, time, then field
        var sortedItems = filteredItems
            .OrderBy(e => ReadString(e, "GameDate"))
            .ThenBy(e => ReadString(e, "StartTime"))
            .ThenBy(e => ReadString(e, "DisplayName"))
            .ToList();

        return new
        {
            items = sortedItems.Select(EntityMappers.MapSlot).ToList(),
            continuationToken = result.ContinuationToken,
            pageSize = result.PageSize,
            hasMore = result.HasMore
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

    private static bool IsWithinDateRange(string gameDate, string fromDate, string toDate)
    {
        if (string.IsNullOrWhiteSpace(fromDate) && string.IsNullOrWhiteSpace(toDate))
            return true;

        if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        if (!string.IsNullOrWhiteSpace(fromDate) &&
            DateOnly.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) &&
            date < from)
            return false;

        if (!string.IsNullOrWhiteSpace(toDate) &&
            DateOnly.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) &&
            date > to)
            return false;

        return true;
    }

    private static string ReadString(TableEntity entity, string key, string defaultValue = "")
    {
        if (!entity.TryGetValue(key, out var value) || value is null) return defaultValue;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? defaultValue : text.Trim();
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
            throw new ApiGuards.HttpError(403, ErrorCodes.UNAUTHORIZED,
                "Not authorized to cancel this slot");
        }

        await _slotRepo.CancelSlotAsync(leagueId, division, slotId);

        _logger.LogInformation("Slot cancelled: {LeagueId}/{Division}/{SlotId} by user {UserId}",
            leagueId, division, slotId, userId);

        // Send notification (fire and forget - don't block response)
        _ = Task.Run(async () =>
        {
            try
            {
                var gameDate = slot.GetString("GameDate") ?? "";
                var startTime = slot.GetString("StartTime") ?? "";
                var message = $"Game slot for {gameDate} at {startTime} has been cancelled.";

                await _notificationService.CreateNotificationAsync(
                    userId,
                    leagueId,
                    "SlotCancelled",
                    message,
                    "#calendar",
                    slotId,
                    "Slot");

                // Also notify confirmed team if one exists
                if (!string.IsNullOrWhiteSpace(confirmedTeamId))
                {
                    // TODO: Get userId for confirmed team coach and notify them
                }
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
                    var coachDivision = (m.GetString("CoachDivision") ?? "").Trim();

                    return string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(coachDivision, division, StringComparison.OrdinalIgnoreCase);
                })
                .Select(m => (
                    userId: m.RowKey,
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
}
