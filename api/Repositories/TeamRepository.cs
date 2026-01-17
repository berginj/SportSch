using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository implementation for team data access.
/// </summary>
public class TeamRepository : ITeamRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<TeamRepository> _logger;

    public TeamRepository(TableServiceClient tableService, ILogger<TeamRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    private static string TeamPk(string leagueId, string division) => $"TEAM|{leagueId}|{division}";

    public async Task<TableEntity?> GetTeamAsync(string leagueId, string division, string teamId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
        var pk = TeamPk(leagueId, division);

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, teamId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<TableEntity>> QueryTeamsByDivisionAsync(string leagueId, string division)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
        var pk = TeamPk(leagueId, division);

        var teams = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(x => x.PartitionKey == pk))
        {
            teams.Add(entity);
        }

        return teams;
    }

    public async Task<List<TableEntity>> QueryAllTeamsAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
        var prefix = $"TEAM|{leagueId}|";
        var next = prefix + "~";
        var filter = $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";

        var teams = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            teams.Add(entity);
        }

        return teams;
    }

    public async Task CreateTeamAsync(TableEntity team)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
        await table.AddEntityAsync(team);
    }

    public async Task UpdateTeamAsync(TableEntity team)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
        await table.UpdateEntityAsync(team, team.ETag, TableUpdateMode.Replace);
    }

    public async Task DeleteTeamAsync(string leagueId, string division, string teamId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
        var pk = TeamPk(leagueId, division);
        await table.DeleteEntityAsync(pk, teamId, ETag.All);
    }
}
