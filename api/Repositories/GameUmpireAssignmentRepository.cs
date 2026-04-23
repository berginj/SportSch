using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of IGameUmpireAssignmentRepository for umpire assignment data access.
/// </summary>
public class GameUmpireAssignmentRepository : IGameUmpireAssignmentRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<GameUmpireAssignmentRepository> _logger;
    private const string TableName = Constants.Tables.GameUmpireAssignments;

    public GameUmpireAssignmentRepository(
        TableServiceClient tableService,
        ILogger<GameUmpireAssignmentRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<List<TableEntity>> GetAssignmentsByGameAsync(string leagueId, string division, string slotId)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(slotId))
            return new List<TableEntity>();

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPASSIGN|{leagueId}|{division}|{slotId}";
        var filter = ODataFilterBuilder.PartitionKeyExact(pk);

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task<List<TableEntity>> GetAssignmentsByUmpireAsync(
        string leagueId,
        string umpireUserId,
        string? dateFrom = null,
        string? dateTo = null)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(umpireUserId))
            return new List<TableEntity>();

        var table = await TableClients.GetTableAsync(_tableService, TableName);

        // Query across all games in league where UmpireUserId matches
        // PartitionKey starts with UMPASSIGN|{leagueId}
        var pkPrefix = $"UMPASSIGN|{leagueId}";

        var filters = new List<string>
        {
            ODataFilterBuilder.PartitionKeyPrefix(pkPrefix),
            ODataFilterBuilder.PropertyEquals("UmpireUserId", umpireUserId)
        };

        if (!string.IsNullOrWhiteSpace(dateFrom))
        {
            filters.Add($"GameDate ge '{ApiGuards.EscapeOData(dateFrom)}'");
        }

        if (!string.IsNullOrWhiteSpace(dateTo))
        {
            filters.Add($"GameDate le '{ApiGuards.EscapeOData(dateTo)}'");
        }

        var filter = ODataFilterBuilder.And(filters.ToArray());

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task<List<TableEntity>> GetAssignmentsByUmpireAndDateAsync(
        string leagueId,
        string umpireUserId,
        string gameDate)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(umpireUserId) || string.IsNullOrWhiteSpace(gameDate))
            return new List<TableEntity>();

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pkPrefix = $"UMPASSIGN|{leagueId}";

        var filter = ODataFilterBuilder.And(
            ODataFilterBuilder.PartitionKeyPrefix(pkPrefix),
            ODataFilterBuilder.PropertyEquals("UmpireUserId", umpireUserId),
            ODataFilterBuilder.PropertyEquals("GameDate", gameDate)
        );

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task<TableEntity?> GetAssignmentAsync(string leagueId, string assignmentId)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(assignmentId))
            return null;

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pkPrefix = $"UMPASSIGN|{leagueId}";

        // Need to query since we don't know the full partition key (missing division and slotId)
        var filter = ODataFilterBuilder.And(
            ODataFilterBuilder.PartitionKeyPrefix(pkPrefix),
            ODataFilterBuilder.PropertyEquals("AssignmentId", assignmentId)
        );

        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter, maxPerPage: 1))
        {
            return entity;
        }

        return null;
    }

    public async Task<TableEntity?> GetAssignmentByGameAndUmpireAsync(
        string leagueId,
        string division,
        string slotId,
        string umpireUserId)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(division) ||
            string.IsNullOrWhiteSpace(slotId) || string.IsNullOrWhiteSpace(umpireUserId))
            return null;

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPASSIGN|{leagueId}|{division}|{slotId}";

        var filter = ODataFilterBuilder.And(
            ODataFilterBuilder.PartitionKeyExact(pk),
            ODataFilterBuilder.PropertyEquals("UmpireUserId", umpireUserId)
        );

        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter, maxPerPage: 1))
        {
            return entity;
        }

        return null;
    }

    public async Task CreateAssignmentAsync(TableEntity assignment)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.AddEntityAsync(assignment);
        _logger.LogInformation("Created umpire assignment {AssignmentId} for game {SlotId}",
            assignment.RowKey, assignment.GetString("SlotId"));
    }

    public async Task UpdateAssignmentAsync(TableEntity assignment, ETag etag)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.UpdateEntityAsync(assignment, etag, TableUpdateMode.Replace);
        _logger.LogInformation("Updated umpire assignment {AssignmentId}", assignment.RowKey);
    }

    public async Task DeleteAssignmentAsync(string leagueId, string division, string slotId, string assignmentId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPASSIGN|{leagueId}|{division}|{slotId}";

        await table.DeleteEntityAsync(pk, assignmentId);
        _logger.LogInformation("Deleted umpire assignment {AssignmentId} from game {SlotId}",
            assignmentId, slotId);
    }

    public async Task<List<TableEntity>> QueryAssignmentsAsync(AssignmentQueryFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.LeagueId))
            return new List<TableEntity>();

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pkPrefix = $"UMPASSIGN|{filter.LeagueId}";

        var filters = new List<string> { ODataFilterBuilder.PartitionKeyPrefix(pkPrefix) };

        if (!string.IsNullOrWhiteSpace(filter.UmpireUserId))
        {
            filters.Add(ODataFilterBuilder.PropertyEquals("UmpireUserId", filter.UmpireUserId));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            filters.Add(ODataFilterBuilder.PropertyEquals("Status", filter.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Division))
        {
            filters.Add(ODataFilterBuilder.PropertyEquals("Division", filter.Division));
        }

        if (!string.IsNullOrWhiteSpace(filter.DateFrom))
        {
            filters.Add($"GameDate ge '{ApiGuards.EscapeOData(filter.DateFrom)}'");
        }

        if (!string.IsNullOrWhiteSpace(filter.DateTo))
        {
            filters.Add($"GameDate le '{ApiGuards.EscapeOData(filter.DateTo)}'");
        }

        var odataFilter = ODataFilterBuilder.And(filters.ToArray());

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: odataFilter))
        {
            result.Add(entity);
            if (result.Count >= filter.PageSize)
                break;
        }

        return result;
    }
}
