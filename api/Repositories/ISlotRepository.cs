using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for slot data access operations.
/// </summary>
public interface ISlotRepository
{
    /// <summary>
    /// Gets a single slot by ID.
    /// </summary>
    Task<TableEntity?> GetSlotAsync(string leagueId, string division, string slotId);

    /// <summary>
    /// Queries slots with filtering and pagination.
    /// </summary>
    Task<PaginationResult<TableEntity>> QuerySlotsAsync(SlotQueryFilter filter, string? continuationToken = null);

    /// <summary>
    /// Checks if a slot conflicts with an existing slot on the same field at the same time.
    /// </summary>
    Task<bool> HasConflictAsync(string leagueId, string fieldKey, string gameDate, int startMin, int endMin, string? excludeSlotId = null);

    /// <summary>
    /// Creates a new slot.
    /// </summary>
    Task CreateSlotAsync(TableEntity slot);

    /// <summary>
    /// Updates an existing slot with ETag concurrency check.
    /// </summary>
    Task UpdateSlotAsync(TableEntity slot, ETag etag);

    /// <summary>
    /// Deletes a slot.
    /// </summary>
    Task DeleteSlotAsync(string leagueId, string division, string slotId);

    /// <summary>
    /// Cancels a slot (sets status to cancelled).
    /// </summary>
    Task CancelSlotAsync(string leagueId, string division, string slotId);

    /// <summary>
    /// Gets all slots for a specific field and date (for conflict checking).
    /// </summary>
    Task<List<TableEntity>> GetSlotsByFieldAndDateAsync(string leagueId, string fieldKey, string gameDate);
}

/// <summary>
/// Filter criteria for querying slots.
/// </summary>
public class SlotQueryFilter
{
    public string LeagueId { get; set; } = "";
    public string? Division { get; set; }
    public string? Status { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string? FieldKey { get; set; }
    public bool? IsExternalOffer { get; set; }
    public int PageSize { get; set; } = 50;
}
