namespace GameSwap.Functions.Services;

/// <summary>
/// Service for authorization checks and role-based access control.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Checks if a user can create a slot in a given division.
    /// </summary>
    Task<bool> CanCreateSlotAsync(string userId, string leagueId, string division, string? teamId);

    /// <summary>
    /// Checks if a user can approve a request for a slot.
    /// </summary>
    Task<bool> CanApproveRequestAsync(string userId, string leagueId, string division, string slotId);

    /// <summary>
    /// Checks if a user can cancel a slot.
    /// </summary>
    Task<bool> CanCancelSlotAsync(string userId, string leagueId, string offeringTeamId, string? confirmedTeamId);

    /// <summary>
    /// Checks if a user can update a slot.
    /// </summary>
    Task<bool> CanUpdateSlotAsync(string userId, string leagueId, string division, string slotId);

    /// <summary>
    /// Gets the user's role in a league.
    /// </summary>
    Task<string> GetUserRoleAsync(string userId, string leagueId);

    /// <summary>
    /// Validates that a user is not a viewer (viewers are read-only).
    /// </summary>
    Task ValidateNotViewerAsync(string userId, string leagueId);

    /// <summary>
    /// Validates that a coach has access to create/modify content for a specific team and division.
    /// </summary>
    Task ValidateCoachAccessAsync(string userId, string leagueId, string division, string? teamId);
}
