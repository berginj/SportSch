using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service for slot business logic and orchestration.
/// </summary>
public interface ISlotService
{
    /// <summary>
    /// Creates a new slot with validation and authorization checks.
    /// </summary>
    Task<object> CreateSlotAsync(CreateSlotRequest request, CorrelationContext context);

    /// <summary>
    /// Gets a single slot by ID.
    /// </summary>
    Task<object?> GetSlotAsync(string leagueId, string division, string slotId);

    /// <summary>
    /// Queries slots with filtering and pagination.
    /// </summary>
    Task<object> QuerySlotsAsync(SlotQueryRequest request, CorrelationContext context);

    /// <summary>
    /// Cancels a slot (marks as cancelled, notifies requesters).
    /// </summary>
    Task CancelSlotAsync(string leagueId, string division, string slotId, string userId);
}

/// <summary>
/// Request model for creating a slot.
/// </summary>
public class CreateSlotRequest
{
    public string Division { get; set; } = "";
    public string OfferingTeamId { get; set; } = "";
    public string? OfferingEmail { get; set; }
    public string GameDate { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public string? FieldKey { get; set; }
    public string? ParkCode { get; set; }
    public string? FieldCode { get; set; }
    public string? ParkName { get; set; }
    public string? FieldName { get; set; }
    public string? GameType { get; set; }
    public string? Notes { get; set; }
    public bool IsExternalOffer { get; set; }
    public bool IsAvailability { get; set; }
}

/// <summary>
/// Request model for querying slots with filters.
/// </summary>
public class SlotQueryRequest
{
    public string LeagueId { get; set; } = "";
    public string? Division { get; set; }
    public string? Status { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string? FieldKey { get; set; }
    public string? ContinuationToken { get; set; }
    public int PageSize { get; set; } = 50;
    public bool ReturnEnvelope { get; set; }
}
