using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Repositories;

/// <summary>
/// Repository for game umpire assignment data access operations.
/// </summary>
public interface IGameUmpireAssignmentRepository
{
    /// <summary>
    /// Gets all assignments for a specific game.
    /// </summary>
    /// <param name="leagueId">League identifier</param>
    /// <param name="division">Division code</param>
    /// <param name="slotId">Game slot ID</param>
    /// <returns>List of assignments for this game (may be empty)</returns>
    Task<List<TableEntity>> GetAssignmentsByGameAsync(string leagueId, string division, string slotId);

    /// <summary>
    /// Gets all assignments for a specific umpire with optional date filtering.
    /// </summary>
    /// <param name="leagueId">League identifier</param>
    /// <param name="umpireUserId">Umpire user ID</param>
    /// <param name="dateFrom">Optional start date filter (YYYY-MM-DD)</param>
    /// <param name="dateTo">Optional end date filter (YYYY-MM-DD)</param>
    Task<List<TableEntity>> GetAssignmentsByUmpireAsync(
        string leagueId,
        string umpireUserId,
        string? dateFrom = null,
        string? dateTo = null);

    /// <summary>
    /// Gets assignments for a specific umpire on a specific date (for conflict detection).
    /// </summary>
    Task<List<TableEntity>> GetAssignmentsByUmpireAndDateAsync(
        string leagueId,
        string umpireUserId,
        string gameDate);

    /// <summary>
    /// Gets a specific assignment by ID.
    /// Requires searching across partitions if assignment ID is known but game is not.
    /// </summary>
    Task<TableEntity?> GetAssignmentAsync(string leagueId, string assignmentId);

    /// <summary>
    /// Gets an assignment by game and umpire (checks if specific umpire already assigned to game).
    /// </summary>
    Task<TableEntity?> GetAssignmentByGameAndUmpireAsync(
        string leagueId,
        string division,
        string slotId,
        string umpireUserId);

    /// <summary>
    /// Creates a new game umpire assignment.
    /// </summary>
    Task CreateAssignmentAsync(TableEntity assignment);

    /// <summary>
    /// Updates an existing assignment with ETag concurrency check.
    /// </summary>
    Task UpdateAssignmentAsync(TableEntity assignment, ETag etag);

    /// <summary>
    /// Deletes an assignment.
    /// </summary>
    Task DeleteAssignmentAsync(string leagueId, string division, string slotId, string assignmentId);

    /// <summary>
    /// Queries assignments with filtering (status, date range, division).
    /// </summary>
    Task<List<TableEntity>> QueryAssignmentsAsync(AssignmentQueryFilter filter);
}

/// <summary>
/// Filter for querying umpire assignments.
/// </summary>
public class AssignmentQueryFilter
{
    public string LeagueId { get; set; } = default!;
    public string? UmpireUserId { get; set; }
    public string? Status { get; set; }
    public string? Division { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public int PageSize { get; set; } = 100;
}
