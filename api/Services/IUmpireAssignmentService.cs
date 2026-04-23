using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service interface for umpire assignment management and conflict detection.
/// </summary>
public interface IUmpireAssignmentService
{
    /// <summary>
    /// Assigns an umpire to a game with conflict detection.
    /// CRITICAL: Prevents double-booking by checking umpire's other assignments.
    /// </summary>
    Task<object> AssignUmpireToGameAsync(AssignUmpireRequest request, CorrelationContext context);

    /// <summary>
    /// Checks if an umpire has conflicting assignments for the given date/time.
    /// Returns list of conflicts (empty if no conflicts).
    /// </summary>
    Task<List<object>> CheckUmpireConflictsAsync(
        string leagueId,
        string umpireUserId,
        string gameDate,
        int startMin,
        int endMin,
        string? excludeSlotId = null);

    /// <summary>
    /// Updates assignment status (umpire accepting/declining or admin cancelling).
    /// </summary>
    Task<object> UpdateAssignmentStatusAsync(
        string assignmentId,
        string newStatus,
        string? reason,
        CorrelationContext context);

    /// <summary>
    /// Removes an umpire assignment (admin only).
    /// </summary>
    Task RemoveAssignmentAsync(string assignmentId, CorrelationContext context);

    /// <summary>
    /// Gets all assignments for a specific umpire with optional filtering.
    /// </summary>
    Task<List<object>> GetUmpireAssignmentsAsync(string umpireUserId, AssignmentQueryFilter filter);

    /// <summary>
    /// Gets all assignments for a specific game.
    /// </summary>
    Task<List<object>> GetGameAssignmentsAsync(string leagueId, string division, string slotId);

    /// <summary>
    /// Gets list of games without umpire assignments (or with declined assignments).
    /// </summary>
    Task<List<object>> GetUnassignedGamesAsync(string leagueId, UnassignedGamesFilter filter);

    /// <summary>
    /// Flags an assignment as a no-show (admin only).
    /// </summary>
    Task FlagNoShowAsync(string assignmentId, string notes, CorrelationContext context);
}

/// <summary>
/// Request model for assigning an umpire to a game.
/// </summary>
public class AssignUmpireRequest
{
    public string LeagueId { get; set; } = default!;
    public string Division { get; set; } = default!;
    public string SlotId { get; set; } = default!;
    public string UmpireUserId { get; set; } = default!;
    public string? Position { get; set; }  // Phase 2: "Home Plate", "Field", "Base"
    public bool SendNotification { get; set; } = true;
}

/// <summary>
/// Filter for querying unassigned games.
/// </summary>
public class UnassignedGamesFilter
{
    public string? Division { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public int PageSize { get; set; } = 100;
}
