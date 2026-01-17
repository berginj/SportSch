using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of IFieldRepository for accessing field data in Azure Table Storage.
/// </summary>
public class FieldRepository : IFieldRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<FieldRepository> _logger;
    private const string TableName = Constants.Tables.Fields;

    public FieldRepository(TableServiceClient tableService, ILogger<FieldRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<TableEntity?> GetFieldAsync(string leagueId, string parkCode, string fieldCode)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.Fields(leagueId, parkCode);
        var rk = Slug.Make(fieldCode);

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, rk);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Field not found: {LeagueId}/{ParkCode}/{FieldCode}", leagueId, parkCode, fieldCode);
            return null;
        }
    }

    public async Task<TableEntity?> GetFieldByKeyAsync(string leagueId, string fieldKey)
    {
        if (!FieldKeyUtil.TryParseFieldKey(fieldKey, out var parkCode, out var fieldCode))
        {
            _logger.LogWarning("Invalid field key format: {FieldKey}", fieldKey);
            return null;
        }

        return await GetFieldAsync(leagueId, parkCode, fieldCode);
    }

    public async Task<List<TableEntity>> QueryFieldsAsync(string leagueId, string? parkCode = null)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        string filter;

        if (!string.IsNullOrEmpty(parkCode))
        {
            // Query specific park
            var pk = Constants.Pk.Fields(leagueId, parkCode);
            filter = ODataFilterBuilder.PartitionKeyExact(pk);
        }
        else
        {
            // Query all parks in league
            var pkPrefix = Constants.Pk.Fields(leagueId, "");
            filter = ODataFilterBuilder.PartitionKeyPrefix(pkPrefix);
        }

        var results = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            results.Add(entity);
        }

        _logger.LogDebug("Retrieved {Count} fields for league {LeagueId}", results.Count, leagueId);
        return results;
    }

    public async Task<bool> FieldExistsAsync(string leagueId, string parkCode, string fieldCode)
    {
        var field = await GetFieldAsync(leagueId, parkCode, fieldCode);
        return field != null;
    }

    public async Task CreateFieldAsync(TableEntity field)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.AddEntityAsync(field);

        _logger.LogInformation("Created field: {PartitionKey}/{RowKey}", field.PartitionKey, field.RowKey);
    }

    public async Task UpdateFieldAsync(TableEntity field)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.UpdateEntityAsync(field, field.ETag, TableUpdateMode.Replace);

        _logger.LogInformation("Updated field: {PartitionKey}/{RowKey}", field.PartitionKey, field.RowKey);
    }

    public async Task DeleteFieldAsync(string leagueId, string parkCode, string fieldCode)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = Constants.Pk.Fields(leagueId, parkCode);
        var rk = Slug.Make(fieldCode);
        await table.DeleteEntityAsync(pk, rk);

        _logger.LogInformation("Deleted field: {LeagueId}/{ParkCode}/{FieldCode}", leagueId, parkCode, fieldCode);
    }

    public async Task DeactivateFieldAsync(string leagueId, string parkCode, string fieldCode)
    {
        var field = await GetFieldAsync(leagueId, parkCode, fieldCode);
        if (field == null)
        {
            throw new InvalidOperationException($"Field not found: {leagueId}/{parkCode}/{fieldCode}");
        }

        field["IsActive"] = false;
        field["UpdatedUtc"] = DateTime.UtcNow;

        await UpdateFieldAsync(field);

        _logger.LogInformation("Deactivated field: {LeagueId}/{ParkCode}/{FieldCode}", leagueId, parkCode, fieldCode);
    }
}
