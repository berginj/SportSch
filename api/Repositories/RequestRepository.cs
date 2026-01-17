using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of IRequestRepository for accessing slot request data.
/// </summary>
public class RequestRepository : IRequestRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<RequestRepository> _logger;
    private const string TableName = Constants.Tables.SlotRequests;

    public RequestRepository(TableServiceClient tableService, ILogger<RequestRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetRequestAsync(string leagueId, string division, string slotId, string requestId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.SlotRequests(leagueId, division, slotId);

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, requestId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Request not found: {LeagueId}/{Division}/{SlotId}/{RequestId}", leagueId, division, slotId, requestId);
            return null;
        }
    }

    public async Task<PaginationResult<TableEntity>> QueryRequestsAsync(RequestQueryFilter filter, string? continuationToken = null)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);

        // Build filter conditions
        var filters = new List<string>();

        // Partition key filter (slot-specific or division-wide or league-wide)
        if (!string.IsNullOrEmpty(filter.SlotId) && !string.IsNullOrEmpty(filter.Division))
        {
            // Specific slot
            var pk = Constants.Pk.SlotRequests(filter.LeagueId, filter.Division, filter.SlotId);
            filters.Add(ODataFilterBuilder.PartitionKeyExact(pk));
        }
        else if (!string.IsNullOrEmpty(filter.Division))
        {
            // Division-wide
            var pkPrefix = Constants.Pk.SlotRequests(filter.LeagueId, filter.Division, "");
            filters.Add(ODataFilterBuilder.PartitionKeyPrefix(pkPrefix));
        }
        else
        {
            // League-wide
            var pkPrefix = $"SLOTREQ|{filter.LeagueId}|";
            filters.Add(ODataFilterBuilder.PartitionKeyPrefix(pkPrefix));
        }

        // Status filter
        if (!string.IsNullOrEmpty(filter.Status))
        {
            filters.Add(ODataFilterBuilder.StatusEquals(filter.Status));
        }

        var filterString = ODataFilterBuilder.And(filters.ToArray());

        // Execute query with pagination
        return await PaginationUtil.QueryWithPaginationAsync(table, filterString, continuationToken, filter.PageSize);
    }

    public async Task<List<TableEntity>> GetPendingRequestsForSlotAsync(string leagueId, string division, string slotId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.SlotRequests(leagueId, division, slotId);

        var filter = ODataFilterBuilder.And(
            ODataFilterBuilder.PartitionKeyExact(pk),
            ODataFilterBuilder.StatusEquals("Pending")
        );

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task CreateRequestAsync(TableEntity request)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.AddEntityAsync(request);

        _logger.LogInformation("Created request: {PartitionKey}/{RowKey}", request.PartitionKey, request.RowKey);
    }

    public async Task UpdateRequestAsync(TableEntity request, ETag etag)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        request.ETag = etag;
        await table.UpdateEntityAsync(request, etag, TableUpdateMode.Replace);

        _logger.LogInformation("Updated request: {PartitionKey}/{RowKey}", request.PartitionKey, request.RowKey);
    }

    public async Task DeleteRequestAsync(string leagueId, string division, string slotId, string requestId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.SlotRequests(leagueId, division, slotId);

        await table.DeleteEntityAsync(pk, requestId);

        _logger.LogInformation("Deleted request: {LeagueId}/{Division}/{SlotId}/{RequestId}", leagueId, division, slotId, requestId);
    }
}
