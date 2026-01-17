using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of IMembershipRepository for accessing membership and authorization data.
/// </summary>
public class MembershipRepository : IMembershipRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<MembershipRepository> _logger;
    private const string MembershipsTable = Constants.Tables.Memberships;
    private const string GlobalAdminsTable = Constants.Tables.GlobalAdmins;

    public MembershipRepository(TableServiceClient tableService, ILogger<MembershipRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetMembershipAsync(string userId, string leagueId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "UNKNOWN") return null;
        if (string.IsNullOrWhiteSpace(leagueId)) return null;

        var table = await TableClients.GetTableAsync(_tableService, MembershipsTable);

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(userId, leagueId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Membership not found: {UserId}/{LeagueId}", userId, leagueId);
            return null;
        }
    }

    public async Task<bool> IsGlobalAdminAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "UNKNOWN") return false;

        var table = await TableClients.GetTableAsync(_tableService, GlobalAdminsTable);
        var pk = "GLOBAL";

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, userId);
            return result.Value != null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> IsMemberAsync(string userId, string leagueId)
    {
        var membership = await GetMembershipAsync(userId, leagueId);
        return membership != null;
    }

    public async Task<List<TableEntity>> GetUserMembershipsAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "UNKNOWN") return new List<TableEntity>();

        var table = await TableClients.GetTableAsync(_tableService, MembershipsTable);
        var filter = ODataFilterBuilder.PartitionKeyExact(userId);

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task<PaginationResult<TableEntity>> QueryLeagueMembershipsAsync(
        string leagueId,
        string? role = null,
        string? continuationToken = null,
        int pageSize = 50)
    {
        var table = await TableClients.GetTableAsync(_tableService, MembershipsTable);

        // Build filter for league memberships
        // Note: Since PK = userId and RK = leagueId, we need to query all rows where RK = leagueId
        var filters = new List<string> { $"RowKey eq '{ApiGuards.EscapeOData(leagueId)}'" };

        if (!string.IsNullOrEmpty(role))
        {
            filters.Add($"Role eq '{ApiGuards.EscapeOData(role)}'");
        }

        var filterString = ODataFilterBuilder.And(filters.ToArray());

        return await PaginationUtil.QueryWithPaginationAsync(table, filterString, continuationToken, pageSize);
    }

    public async Task CreateMembershipAsync(TableEntity membership)
    {
        var table = await TableClients.GetTableAsync(_tableService, MembershipsTable);
        await table.AddEntityAsync(membership);

        _logger.LogInformation("Created membership: {UserId}/{LeagueId}", membership.PartitionKey, membership.RowKey);
    }

    public async Task UpdateMembershipAsync(TableEntity membership)
    {
        var table = await TableClients.GetTableAsync(_tableService, MembershipsTable);
        await table.UpdateEntityAsync(membership, membership.ETag, TableUpdateMode.Replace);

        _logger.LogInformation("Updated membership: {UserId}/{LeagueId}", membership.PartitionKey, membership.RowKey);
    }

    public async Task DeleteMembershipAsync(string userId, string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, MembershipsTable);
        await table.DeleteEntityAsync(userId, leagueId);

        _logger.LogInformation("Deleted membership: {UserId}/{LeagueId}", userId, leagueId);
    }

    public async Task UpsertMembershipAsync(TableEntity membership)
    {
        var table = await TableClients.GetTableAsync(_tableService, MembershipsTable);
        await table.UpsertEntityAsync(membership, TableUpdateMode.Merge);

        _logger.LogInformation("Upserted membership: {UserId}/{LeagueId}", membership.PartitionKey, membership.RowKey);
    }

    public async Task<List<TableEntity>> QueryAllMembershipsAsync(string? leagueFilter = null)
    {
        var table = await TableClients.GetTableAsync(_tableService, MembershipsTable);
        string? filter = null;

        if (!string.IsNullOrWhiteSpace(leagueFilter))
        {
            filter = $"RowKey eq '{ApiGuards.EscapeOData(leagueFilter)}'";
        }

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task<List<TableEntity>> GetLeagueMembershipsAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);

        // Query using the partition key pattern
        var pkPrefix = $"MEMBERSHIP|{leagueId}|";
        var filter = ODataFilterBuilder.PartitionKeyPrefix(pkPrefix);

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        _logger.LogDebug("Retrieved {Count} memberships for league {LeagueId}", result.Count, leagueId);
        return result;
    }
}
