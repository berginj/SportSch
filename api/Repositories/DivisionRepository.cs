using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository implementation for division data access.
/// </summary>
public class DivisionRepository : IDivisionRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<DivisionRepository> _logger;

    public DivisionRepository(TableServiceClient tableService, ILogger<DivisionRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetDivisionAsync(string leagueId, string code)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Divisions);
        var pk = Constants.Pk.Divisions(leagueId);

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, code);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<TableEntity>> QueryDivisionsAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Divisions);
        var pk = Constants.Pk.Divisions(leagueId);

        var divisions = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(x => x.PartitionKey == pk))
        {
            divisions.Add(entity);
        }

        return divisions;
    }

    public async Task CreateDivisionAsync(TableEntity division)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Divisions);
        await table.AddEntityAsync(division);
    }

    public async Task UpdateDivisionAsync(TableEntity division)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Divisions);
        await table.UpdateEntityAsync(division, division.ETag, TableUpdateMode.Replace);
    }

    public async Task<TableEntity?> GetTemplatesAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Divisions);
        var pk = $"DIVTEMPLATE|{leagueId}";
        var rk = "CATALOG";

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, rk);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertTemplatesAsync(TableEntity templates)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Divisions);
        await table.UpsertEntityAsync(templates, TableUpdateMode.Replace);
    }
}
