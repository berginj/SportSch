using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository implementation for practice request data access.
/// </summary>
public class PracticeRequestRepository : IPracticeRequestRepository
{
    private readonly TableServiceClient _tableService;

    private static string Pk(string leagueId) => $"PRACTICEREQ|{leagueId}";

    public PracticeRequestRepository(
        TableServiceClient tableService)
    {
        _tableService = tableService;
    }

    public async Task CreateRequestAsync(TableEntity request)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.PracticeRequests);
        await table.AddEntityAsync(request);
    }

    public async Task<TableEntity?> GetRequestAsync(string leagueId, string requestId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.PracticeRequests);
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
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.PracticeRequests);
        request.ETag = etag;
        await table.UpdateEntityAsync(request, etag, TableUpdateMode.Replace);
    }

    public async Task<List<TableEntity>> QueryRequestsAsync(
        string leagueId,
        string? status = null,
        string? division = null,
        string? teamId = null,
        string? slotId = null)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.PracticeRequests);
        var filters = new List<string>
        {
            $"PartitionKey eq '{ApiGuards.EscapeOData(Pk(leagueId))}'"
        };

        if (!string.IsNullOrWhiteSpace(status))
            filters.Add($"Status eq '{ApiGuards.EscapeOData(status)}'");
        if (!string.IsNullOrWhiteSpace(division))
            filters.Add($"Division eq '{ApiGuards.EscapeOData(division)}'");
        if (!string.IsNullOrWhiteSpace(teamId))
            filters.Add($"TeamId eq '{ApiGuards.EscapeOData(teamId)}'");
        if (!string.IsNullOrWhiteSpace(slotId))
            filters.Add($"SlotId eq '{ApiGuards.EscapeOData(slotId)}'");

        var filter = string.Join(" and ", filters);
        var list = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(entity);
        }

        return list;
    }

    public async Task<int> CountRequestsForTeamAsync(
        string leagueId,
        string division,
        string teamId,
        IReadOnlyCollection<string> statuses)
    {
        var entities = await QueryRequestsAsync(leagueId, null, division, teamId, null);
        if (statuses == null || statuses.Count == 0)
            return entities.Count;

        var allowed = new HashSet<string>(statuses.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        return entities.Count(e => allowed.Contains((e.GetString("Status") ?? "").Trim()));
    }

    public async Task<bool> ExistsRequestForTeamSlotAsync(
        string leagueId,
        string division,
        string teamId,
        string slotId,
        IReadOnlyCollection<string> statuses)
    {
        var entities = await QueryRequestsAsync(leagueId, null, division, teamId, slotId);
        if (statuses == null || statuses.Count == 0)
            return entities.Count > 0;

        var allowed = new HashSet<string>(statuses.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        return entities.Any(e => allowed.Contains((e.GetString("Status") ?? "").Trim()));
    }

    public async Task<List<TableEntity>> QuerySlotRequestsAsync(
        string leagueId,
        string division,
        string slotId,
        IReadOnlyCollection<string>? statuses = null)
    {
        var entities = await QueryRequestsAsync(leagueId, null, division, null, slotId);
        if (statuses == null || statuses.Count == 0)
            return entities;

        var allowed = new HashSet<string>(statuses.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        return entities.Where(e => allowed.Contains((e.GetString("Status") ?? "").Trim())).ToList();
    }
}
