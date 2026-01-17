using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository implementation for schedule run data access.
/// </summary>
public class ScheduleRunRepository : IScheduleRunRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<ScheduleRunRepository> _logger;

    public ScheduleRunRepository(TableServiceClient tableService, ILogger<ScheduleRunRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetScheduleRunAsync(string leagueId, string division, string runId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.ScheduleRuns);
        var pk = Constants.Pk.ScheduleRuns(leagueId, division);

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, runId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<TableEntity>> QueryScheduleRunsAsync(string leagueId, string division)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.ScheduleRuns);
        var pk = Constants.Pk.ScheduleRuns(leagueId, division);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";

        var runs = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            runs.Add(entity);
        }

        return runs;
    }

    public async Task CreateScheduleRunAsync(TableEntity scheduleRun)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.ScheduleRuns);
        await table.AddEntityAsync(scheduleRun);

        _logger.LogInformation("Created schedule run: {LeagueId}/{Division}/{RunId}",
            scheduleRun.GetString("LeagueId"), scheduleRun.GetString("Division"), scheduleRun.RowKey);
    }
}
