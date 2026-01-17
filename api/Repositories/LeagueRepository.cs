using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository implementation for league data access.
/// </summary>
public class LeagueRepository : ILeagueRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<LeagueRepository> _logger;

    public LeagueRepository(TableServiceClient tableService, ILogger<LeagueRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetLeagueAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
        var pk = Constants.Pk.Leagues;

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, leagueId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<TableEntity>> QueryLeaguesAsync(bool includeAll = false)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
        var pk = Constants.Pk.Leagues;

        var leagues = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(x => x.PartitionKey == pk))
        {
            if (!includeAll)
            {
                var status = (entity.GetString("Status") ?? "Active").Trim();
                if (string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Deleted", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            leagues.Add(entity);
        }

        return leagues;
    }

    public async Task CreateLeagueAsync(TableEntity league)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
        await table.AddEntityAsync(league);

        _logger.LogInformation("Created league: {LeagueId}", league.RowKey);
    }

    public async Task UpdateLeagueAsync(TableEntity league)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
        await table.UpdateEntityAsync(league, league.ETag, TableUpdateMode.Replace);

        _logger.LogInformation("Updated league: {LeagueId}", league.RowKey);
    }

    public async Task DeleteLeagueAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
        var pk = Constants.Pk.Leagues;
        await table.DeleteEntityAsync(pk, leagueId);

        _logger.LogInformation("Deleted league: {LeagueId}", leagueId);
    }
}
