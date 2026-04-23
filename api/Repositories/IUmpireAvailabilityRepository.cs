using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for umpire availability and blackout rules.
/// </summary>
public interface IUmpireAvailabilityRepository
{
    /// <summary>
    /// Gets all availability rules for a specific umpire.
    /// </summary>
    Task<List<TableEntity>> GetAvailabilityRulesAsync(string leagueId, string umpireUserId);

    /// <summary>
    /// Gets a specific availability rule by ID.
    /// </summary>
    Task<TableEntity?> GetAvailabilityRuleAsync(string leagueId, string umpireUserId, string ruleId);

    /// <summary>
    /// Creates a new availability or blackout rule.
    /// </summary>
    Task CreateAvailabilityRuleAsync(TableEntity rule);

    /// <summary>
    /// Deletes an availability rule.
    /// </summary>
    Task DeleteAvailabilityRuleAsync(string leagueId, string umpireUserId, string ruleId);

    /// <summary>
    /// Gets availability rules that apply to a specific date.
    /// Used for checking if umpire is available on a given day.
    /// </summary>
    Task<List<TableEntity>> GetRulesForDateAsync(string leagueId, string umpireUserId, string gameDate);
}
