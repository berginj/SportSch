using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Implementation of IUmpireAvailabilityRepository for availability rule data access.
/// </summary>
public class UmpireAvailabilityRepository : IUmpireAvailabilityRepository
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<UmpireAvailabilityRepository> _logger;
    private const string TableName = Constants.Tables.UmpireAvailability;

    public UmpireAvailabilityRepository(
        TableServiceClient tableService,
        ILogger<UmpireAvailabilityRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task<List<TableEntity>> GetAvailabilityRulesAsync(string leagueId, string umpireUserId)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(umpireUserId))
            return new List<TableEntity>();

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPAVAIL|{leagueId}|{umpireUserId}";
        var filter = ODataFilterBuilder.PartitionKeyExact(pk);

        var result = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task<TableEntity?> GetAvailabilityRuleAsync(string leagueId, string umpireUserId, string ruleId)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(umpireUserId) || string.IsNullOrWhiteSpace(ruleId))
            return null;

        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPAVAIL|{leagueId}|{umpireUserId}";

        try
        {
            var result = await table.GetEntityAsync<TableEntity>(pk, ruleId);
            return result.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Availability rule not found: {RuleId}", ruleId);
            return null;
        }
    }

    public async Task CreateAvailabilityRuleAsync(TableEntity rule)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        await table.AddEntityAsync(rule);
        _logger.LogInformation("Created availability rule {RuleId} for umpire {UmpireUserId}",
            rule.RowKey, rule.GetString("UmpireUserId"));
    }

    public async Task DeleteAvailabilityRuleAsync(string leagueId, string umpireUserId, string ruleId)
    {
        var table = await TableClients.GetTableAsync(_tableService, TableName);
        var pk = $"UMPAVAIL|{leagueId}|{umpireUserId}";

        await table.DeleteEntityAsync(pk, ruleId);
        _logger.LogInformation("Deleted availability rule {RuleId} for umpire {UmpireUserId}",
            ruleId, umpireUserId);
    }

    public async Task<List<TableEntity>> GetRulesForDateAsync(string leagueId, string umpireUserId, string gameDate)
    {
        if (string.IsNullOrWhiteSpace(leagueId) || string.IsNullOrWhiteSpace(umpireUserId) || string.IsNullOrWhiteSpace(gameDate))
            return new List<TableEntity>();

        var allRules = await GetAvailabilityRulesAsync(leagueId, umpireUserId);

        // Filter rules that apply to this date
        var applicableRules = allRules.Where(rule =>
        {
            var dateFrom = rule.GetString("DateFrom") ?? "";
            var dateTo = rule.GetString("DateTo") ?? "";

            // Check if gameDate falls within rule's date range
            return string.Compare(gameDate, dateFrom, StringComparison.Ordinal) >= 0 &&
                   string.Compare(gameDate, dateTo, StringComparison.Ordinal) <= 0;
        }).ToList();

        return applicableRules;
    }
}
