using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository implementation for access request data access.
/// </summary>
public class AccessRequestRepository : IAccessRequestRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<AccessRequestRepository> _logger;

    public AccessRequestRepository(TableServiceClient tableService, ILogger<AccessRequestRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    private static string ReqPk(string leagueId) => Constants.Pk.AccessRequests(leagueId);

    public async Task<TableEntity?> GetAccessRequestAsync(string leagueId, string userId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.AccessRequests);
        var pk = ReqPk(leagueId);

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, userId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<TableEntity>> QueryAccessRequestsByUserIdAsync(string userId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.AccessRequests);
        var filter = $"UserId eq '{ApiGuards.EscapeOData(userId)}'";

        var requests = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            requests.Add(entity);
        }

        return requests;
    }

    public async Task<List<TableEntity>> QueryAccessRequestsByLeagueAsync(string leagueId, string? status = null)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.AccessRequests);
        var pk = ReqPk(leagueId);

        var filters = new List<string> { $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'" };
        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add($"Status eq '{ApiGuards.EscapeOData(status)}'");
        }

        var filterString = ODataFilterBuilder.And(filters.ToArray());

        var requests = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filterString))
        {
            requests.Add(entity);
        }

        return requests;
    }

    public async Task<List<TableEntity>> QueryAllAccessRequestsAsync(string? status = null)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.AccessRequests);

        string? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            filter = $"Status eq '{ApiGuards.EscapeOData(status)}'";
        }

        var requests = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            requests.Add(entity);
        }

        return requests;
    }

    public async Task UpsertAccessRequestAsync(TableEntity accessRequest)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.AccessRequests);
        await table.UpsertEntityAsync(accessRequest, TableUpdateMode.Replace);

        _logger.LogInformation("Upserted access request: {LeagueId}/{UserId}",
            accessRequest.GetString("LeagueId"), accessRequest.RowKey);
    }

    public async Task UpdateAccessRequestAsync(TableEntity accessRequest)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.AccessRequests);
        await table.UpdateEntityAsync(accessRequest, accessRequest.ETag, TableUpdateMode.Replace);

        _logger.LogInformation("Updated access request: {LeagueId}/{UserId}",
            accessRequest.GetString("LeagueId"), accessRequest.RowKey);
    }
}
