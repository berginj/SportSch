using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository implementation for game reschedule request data access.
/// </summary>
public class GameRescheduleRequestRepository : IGameRescheduleRequestRepository
{
    private readonly TableServiceClient _tableService;

    private static string Pk(string leagueId) => $"GAMERESCHEDULE|{leagueId}";

    public GameRescheduleRequestRepository(TableServiceClient tableService)
    {
        _tableService = tableService;
    }

    public async Task CreateRequestAsync(TableEntity request)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.GameRescheduleRequests);
        await table.AddEntityAsync(request);
    }

    public async Task<TableEntity?> GetRequestAsync(string leagueId, string requestId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.GameRescheduleRequests);
        try
        {
            var result = await table.GetEntityAsync<TableEntity>(Pk(leagueId), requestId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpdateRequestAsync(TableEntity request, ETag etag)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.GameRescheduleRequests);
        request.ETag = etag;
        await table.UpdateEntityAsync(request, etag, TableUpdateMode.Replace);
    }

    public async Task<List<TableEntity>> QueryRequestsAsync(
        string leagueId,
        string? status = null,
        string? teamId = null)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.GameRescheduleRequests);
        var filters = new List<string>
        {
            $"PartitionKey eq '{ApiGuards.EscapeOData(Pk(leagueId))}'"
        };

        if (!string.IsNullOrWhiteSpace(status))
            filters.Add($"Status eq '{ApiGuards.EscapeOData(status)}'");

        // Filter by team involvement (requesting OR opponent)
        if (!string.IsNullOrWhiteSpace(teamId))
        {
            filters.Add($"(RequestingTeamId eq '{ApiGuards.EscapeOData(teamId)}' or OpponentTeamId eq '{ApiGuards.EscapeOData(teamId)}')");
        }

        var filter = string.Join(" and ", filters);
        var list = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(entity);
        }

        return list;
    }

    public async Task<List<TableEntity>> QueryRequestsBySlotAsync(
        string leagueId,
        string division,
        string slotId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.GameRescheduleRequests);
        var filter = $"PartitionKey eq '{ApiGuards.EscapeOData(Pk(leagueId))}' and OriginalSlotId eq '{ApiGuards.EscapeOData(slotId)}'";

        var list = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(entity);
        }

        return list;
    }

    public async Task<bool> HasActiveRequestForSlotAsync(
        string leagueId,
        string division,
        string slotId,
        IReadOnlyCollection<string> activeStatuses)
    {
        var requests = await QueryRequestsBySlotAsync(leagueId, division, slotId);
        if (activeStatuses == null || activeStatuses.Count == 0)
            return requests.Count > 0;

        var allowed = new HashSet<string>(activeStatuses.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        return requests.Any(e => allowed.Contains((e.GetString("Status") ?? "").Trim()));
    }
}
