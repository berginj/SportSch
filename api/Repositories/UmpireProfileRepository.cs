using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of IUmpireProfileRepository for umpire profile data access.
/// </summary>
public class UmpireProfileRepository : IUmpireProfileRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<UmpireProfileRepository> _logger;
    private const string TableName = Constants.Tables.UmpireProfiles;

    public UmpireProfileRepository(
        TableServiceClient tableService,
        ILogger<UmpireProfileRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetUmpireAsync(string leagueId, string umpireUserId)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(umpireUserId))
            return null;

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPIRE|{leagueId}";

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, umpireUserId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Umpire not found: {LeagueId}/{UmpireUserId}", leagueId, umpireUserId);
            return null;
        }
    }

    public async Task<List<TableEntity>> QueryUmpiresAsync(string leagueId, bool? activeOnly = null)
    {
        if (string.IsNullOrWhiteSpace(leagueId))
            return new List<TableEntity>();

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPIRE|{leagueId}";

        var filters = new List<string> { ODataFilterBuilder.PartitionKeyExact(pk) };

        if (activeOnly == true)
        {
            filters.Add(ODataFilterBuilder.PropertyEqualsBool("IsActive", true));
        }

        var filter = ODataFilterBuilder.And(filters.ToArray());

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task CreateUmpireAsync(TableEntity umpire)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.AddEntityAsync(umpire);
        _logger.LogInformation("Created umpire profile: {UmpireUserId} in league {LeagueId}",
            umpire.RowKey, umpire.GetString("LeagueId"));
    }

    public async Task UpdateUmpireAsync(TableEntity umpire, ETag etag)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.UpdateEntityAsync(umpire, etag, TableUpdateMode.Replace);
        _logger.LogInformation("Updated umpire profile: {UmpireUserId}", umpire.RowKey);
    }

    public async Task DeleteUmpireAsync(string leagueId, string umpireUserId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPIRE|{leagueId}";

        await table.DeleteEntityAsync(pk, umpireUserId);
        _logger.LogInformation("Deleted umpire profile: {UmpireUserId} from league {LeagueId}",
            umpireUserId, leagueId);
    }

    public async Task<List<TableEntity>> SearchUmpiresByNameAsync(string leagueId, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(searchTerm))
            return new List<TableEntity>();

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPIRE|{leagueId}";

        // Get all umpires in league, filter client-side by name
        // Table Storage doesn't support LIKE queries, so we fetch all and filter
        var filter = ODataFilterBuilder.PartitionKeyExact(pk);

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            var name = (entity.GetString("Name") ?? "").ToLowerInvariant();
            if (name.Contains(searchTerm.ToLowerInvariant()))
            {
                result.Add(entity);
            }
        }

        return result;
    }
}
