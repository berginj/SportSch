using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of ISlotRepository for accessing slot data in Table Storage.
/// </summary>
public class SlotRepository : ISlotRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<SlotRepository> _logger;
    private const string TableName = Constants.Tables.Slots;

    public SlotRepository(TableServiceClient tableService, ILogger<SlotRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetSlotAsync(string leagueId, string division, string slotId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.Slots(leagueId, division);

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, slotId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Slot not found: {LeagueId}/{Division}/{SlotId}", leagueId, division, slotId);
            return null;
        }
    }

    public async Task<PaginationResult<TableEntity>> QuerySlotsAsync(SlotQueryFilter filter, string? continuationToken = null)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);

        // Build filter conditions
        var filters = new List<string>();

        // Partition key filter (division-specific or league-wide)
        if (!string.IsNullOrEmpty(filter.Division))
        {
            var pk = Constants.Pk.Slots(filter.LeagueId, filter.Division);
            filters.Add(ODataFilterBuilder.PartitionKeyExact(pk));
        }
        else
        {
            var pkPrefix = Constants.Pk.Slots(filter.LeagueId, "");
            filters.Add(ODataFilterBuilder.PartitionKeyPrefix(pkPrefix));
        }

        // Status filter
        if (!string.IsNullOrEmpty(filter.Status))
        {
            filters.Add(ODataFilterBuilder.StatusEquals(filter.Status));
        }

        // Date range filters
        if (!string.IsNullOrEmpty(filter.FromDate))
        {
            filters.Add($"GameDate ge '{ApiGuards.EscapeOData(filter.FromDate)}'");
        }

        if (!string.IsNullOrEmpty(filter.ToDate))
        {
            filters.Add($"GameDate le '{ApiGuards.EscapeOData(filter.ToDate)}'");
        }

        // Field filter
        if (!string.IsNullOrEmpty(filter.FieldKey))
        {
            filters.Add($"FieldKey eq '{ApiGuards.EscapeOData(filter.FieldKey)}'");
        }

        var filterString = ODataFilterBuilder.And(filters.ToArray());

        // Execute query with pagination
        return await PaginationUtil.QueryWithPaginationAsync(table, filterString, continuationToken, filter.PageSize);
    }

    public async Task<bool> HasConflictAsync(string leagueId, string fieldKey, string gameDate, int startMin, int endMin)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);

        // Query all slots for this league on the same date and field
        var prefix = $"SLOT|{leagueId}|";
        var next = prefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}' " +
                     $"and GameDate eq '{ApiGuards.EscapeOData(gameDate)}' " +
                     $"and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";

        await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
        {
            // Skip cancelled slots
            var status = (e.GetString("Status") ?? Constants.Status.SlotOpen).Trim();
            if (string.Equals(status, Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check for time overlap
            if (!TimeUtil.TryParseMinutes(e.GetString("StartTime"), out var existingStart))
                continue;

            if (!TimeUtil.TryParseMinutes(e.GetString("EndTime"), out var existingEnd))
                continue;

            if (existingStart >= existingEnd) continue; // Invalid time range

            // Check if times overlap
            if (TimeUtil.Overlaps(startMin, endMin, existingStart, existingEnd))
            {
                _logger.LogWarning("Slot conflict detected: {FieldKey} on {GameDate} {StartMin}-{EndMin} overlaps with existing slot",
                    fieldKey, gameDate, startMin, endMin);
                return true;
            }
        }

        return false;
    }

    public async Task<List<TableEntity>> GetSlotsForFieldAndDateAsync(string leagueId, string fieldKey, string gameDate)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);

        var prefix = $"SLOT|{leagueId}|";
        var next = prefix + "\uffff";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}' " +
                     $"and GameDate eq '{ApiGuards.EscapeOData(gameDate)}' " +
                     $"and FieldKey eq '{ApiGuards.EscapeOData(fieldKey)}'";

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task CreateSlotAsync(TableEntity slot)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.AddEntityAsync(slot);

        _logger.LogInformation("Created slot: {PartitionKey}/{RowKey}", slot.PartitionKey, slot.RowKey);
    }

    public async Task UpdateSlotAsync(TableEntity slot, ETag etag)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        slot.ETag = etag;
        await table.UpdateEntityAsync(slot, etag, TableUpdateMode.Replace);

        _logger.LogInformation("Updated slot: {PartitionKey}/{RowKey}", slot.PartitionKey, slot.RowKey);
    }

    public async Task CancelSlotAsync(string leagueId, string division, string slotId)
    {
        var slot = await GetSlotAsync(leagueId, division, slotId);
        if (slot == null)
        {
            throw new InvalidOperationException($"Slot not found: {leagueId}/{division}/{slotId}");
        }

        slot["Status"] = Constants.Status.SlotCancelled;
        slot["UpdatedUtc"] = DateTimeOffset.UtcNow;

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.UpdateEntityAsync(slot, slot.ETag, TableUpdateMode.Replace);

        _logger.LogInformation("Cancelled slot: {LeagueId}/{Division}/{SlotId}", leagueId, division, slotId);
    }
}
