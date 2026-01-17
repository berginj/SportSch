using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository interface for schedule run data access.
/// </summary>
public interface IScheduleRunRepository
{
    /// <summary>
    /// Gets a schedule run by runId.
    /// </summary>
    Task<TableEntity?> GetScheduleRunAsync(string leagueId, string division, string runId);

    /// <summary>
    /// Queries schedule runs for a league/division.
    /// </summary>
    Task<List<TableEntity>> QueryScheduleRunsAsync(string leagueId, string division);

    /// <summary>
    /// Creates a new schedule run.
    /// </summary>
    Task CreateScheduleRunAsync(TableEntity scheduleRun);
}
