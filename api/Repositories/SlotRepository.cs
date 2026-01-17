using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of ISlotRepository for accessing slot data in Azure Table Storage.
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
        var filters = new List<string>();

        // Partition key filter (league + division)
        if (!string.IsNullOrEmpty(filter.Division))
        {
            var pk = Constants.Pk.Slots(filter.LeagueId, filter.Division);
            filters.Add(ODataFilterBuilder.PartitionKeyExact(pk));
        }
        else
        {
            // Query all divisions in league
            var pkPrefix = Constants.Pk.Slots(filter.LeagueId, "");
            filters.Add(ODataFilterBuilder.PartitionKeyPrefix(pkPrefix));
        }

        // Status filter
        if (!string.IsNullOrEmpty(filter.Status))
        {
            filters.Add(ODataFilterBuilder.StatusEquals(filter.Status));
        }

        // Date range filter
        if (!string.IsNullOrEmpty(filter.FromDate) || !string.IsNullOrEmpty(filter.ToDate))
        {
            filters.Add(ODataFilterBuilder.DateRange("GameDate", filter.FromDate, filter.ToDate));
        }

        // Field key filter
        if (!string.IsNullOrEmpty(filter.FieldKey))
        {
            filters.Add(ODataFilterBuilder.PropertyEquals("FieldKey", filter.FieldKey));
        }

        // External offer filter
        if (filter.IsExternalOffer.HasValue)
        {
            filters.Add($"IsExternalOffer eq {filter.IsExternalOffer.Value.ToString().ToLower()}");
        }

        var filterString = ODataFilterBuilder.And(filters.ToArray());

        _logger.LogDebug("Querying slots with filter: {Filter}", filterString);

        return await PaginationUtil.QueryWithPaginationAsync(table, filterString, continuationToken, filter.PageSize);
    }

    public async Task<bool> HasConflictAsync(string leagueId, string fieldKey, string gameDate, int startMin, int endMin, string? excludeSlotId = null)
    {
        var slots = await GetSlotsByFieldAndDateAsync(leagueId, fieldKey, gameDate);

        foreach (var slot in slots)
        {
            // Skip the slot we're updating (if provided)
            if (!string.IsNullOrEmpty(excludeSlotId) && slot.RowKey == excludeSlotId)
                continue;

            // Skip cancelled slots
            var status = slot.GetString("Status") ?? "";
            if (status == Constants.Status.SlotCancelled)
                continue;

            // Check time overlap
            var slotStartMin = slot.GetInt32("StartMin") ?? 0;
            var slotEndMin = slot.GetInt32("EndMin") ?? 0;

            // Times conflict if they overlap
            var hasOverlap = startMin < slotEndMin && endMin > slotStartMin;
            if (hasOverlap)
            {
                _logger.LogWarning("Slot conflict detected: {FieldKey} on {GameDate} at {StartMin}-{EndMin} conflicts with {SlotId}",
                    fieldKey, gameDate, startMin, endMin, slot.RowKey);
                return true;
            }
        }

        return false;
    }

    public async Task<List<TableEntity>> GetSlotsByFieldAndDateAsync(string leagueId, string fieldKey, string gameDate)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);

        // Query across all divisions in the league
        var pkPrefix = Constants.Pk.Slots(leagueId, "");
        var filters = new[]
        {
            ODataFilterBuilder.PartitionKeyPrefix(pkPrefix),
            ODataFilterBuilder.PropertyEquals("FieldKey", fieldKey),
            ODataFilterBuilder.PropertyEquals("GameDate", gameDate)
        };

        var filterString = ODataFilterBuilder.And(filters);

        var results = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filterString))
        {
            results.Add(entity);
        }

        return results;
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

    public async Task DeleteSlotAsync(string leagueId, string division, string slotId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.Slots(leagueId, division);
        await table.DeleteEntityAsync(pk, slotId);

        _logger.LogInformation("Deleted slot: {LeagueId}/{Division}/{SlotId}", leagueId, division, slotId);
    }

    public async Task CancelSlotAsync(string leagueId, string division, string slotId)
    {
        var slot = await GetSlotAsync(leagueId, division, slotId);
        if (slot == null)
        {
            throw new InvalidOperationException($"Slot not found: {leagueId}/{division}/{slotId}");
        }

        slot["Status"] = Constants.Status.SlotCancelled;
        slot["UpdatedUtc"] = DateTime.UtcNow;

        await UpdateSlotAsync(slot, slot.ETag);

        _logger.LogInformation("Cancelled slot: {LeagueId}/{Division}/{SlotId}", leagueId, division, slotId);
    }
}
