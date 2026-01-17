namespace GameSwap.Functions.Services;

/// <summary>
/// Service for centralized authorization logic.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Checks if a user can create a slot in a specific division/team.
    /// </summary>
    Task<bool> CanCreateSlotAsync(string userId, string leagueId, string division, string? teamId);

    /// <summary>
    /// Checks if a user can approve a slot request.
    /// </summary>
    Task<bool> CanApproveRequestAsync(string userId, string leagueId, string division, string slotId);

    /// <summary>
    /// Checks if a user can cancel a slot (checks team ownership).
    /// </summary>
    Task<bool> CanCancelSlotAsync(string userId, string leagueId, string offeringTeamId, string? confirmedTeamId);

    /// <summary>
    /// Gets the user's role in a league.
    /// </summary>
    Task<string> GetUserRoleAsync(string userId, string leagueId);

    /// <summary>
    /// Gets the user's coach assignment (division and team).
    /// </summary>
    Task<(string division, string teamId)> GetCoachAssignmentAsync(string userId, string leagueId);
}
