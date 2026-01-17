using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of ISlotService for slot business logic.
/// </summary>
public class SlotService : ISlotService
{
    private readonly ISlotRepository _slotRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly IAuthorizationService _authService;
    private readonly ILogger<SlotService> _logger;

    public SlotService(
        ISlotRepository slotRepo,
        IFieldRepository fieldRepo,
        IAuthorizationService authService,
        ILogger<SlotService> logger)
    {
        _slotRepo = slotRepo;
        _fieldRepo = fieldRepo;
        _authService = authService;
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
        var filter = new SlotQueryFilter
        {
            LeagueId = request.LeagueId,
            Division = request.Division,
            Status = request.Status,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FieldKey = request.FieldKey,
            PageSize = request.PageSize
        };

        var result = await _slotRepo.QuerySlotsAsync(filter, request.ContinuationToken);

        return new
        {
            items = result.Items.Select(EntityMappers.MapSlot).ToList(),
            continuationToken = result.ContinuationToken,
            pageSize = result.PageSize,
            hasMore = result.HasMore
        };
    }

    public async Task CancelSlotAsync(string leagueId, string division, string slotId, string userId)
    {
        // Authorization check
        if (!await _authService.CanCancelSlotAsync(userId, leagueId, division, slotId))
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.UNAUTHORIZED,
                "Not authorized to cancel this slot");
        }

        await _slotRepo.CancelSlotAsync(leagueId, division, slotId);

        _logger.LogInformation("Slot cancelled: {LeagueId}/{Division}/{SlotId} by user {UserId}",
            leagueId, division, slotId, userId);
    }
}
