using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of IAuthorizationService for centralized authorization logic.
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly IMembershipRepository _membershipRepo;
    private readonly ILogger<AuthorizationService> _logger;

    public AuthorizationService(IMembershipRepository membershipRepo, ILogger<AuthorizationService> logger)
    {
        _membershipRepo = membershipRepo;
        _logger = logger;
    }

    public async Task<bool> CanCreateSlotAsync(string userId, string leagueId, string division, string? teamId)
    {
        // Global admins can create any slot
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
            return true;

        // Get user's membership
        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        if (membership == null)
        {
            _logger.LogWarning("User {UserId} attempted to create slot without membership in league {LeagueId}", userId, leagueId);
            return false;
        }

        var role = (membership.GetString("Role") ?? Constants.Roles.Viewer).Trim();

        // League admins can create any slot
        if (string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            return true;

        // Coaches can only create slots for their assigned team/division
        if (string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
        {
            var coachDivision = (membership.GetString("CoachDivision") ?? "").Trim();
            var coachTeamId = (membership.GetString("CoachTeamId") ?? "").Trim();

            if (string.IsNullOrWhiteSpace(coachTeamId))
            {
                _logger.LogWarning("Coach {UserId} has no assigned team", userId);
                return false;
            }

            if (!string.Equals(coachDivision, division, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Coach {UserId} attempted to create slot in different division", userId);
                return false;
            }

            if (!string.Equals(coachTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Coach {UserId} attempted to create slot for different team", userId);
                return false;
            }

            return true;
        }

        // Viewers cannot create slots
        return false;
    }

    public async Task<bool> CanApproveRequestAsync(string userId, string leagueId, string division, string slotId)
    {
        // Similar logic to CanCreateSlotAsync - user must be the slot owner or admin
        // For simplicity, checking role here
        var role = await GetUserRoleAsync(userId, leagueId);
        return !string.Equals(role, Constants.Roles.Viewer, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> CanCancelSlotAsync(string userId, string leagueId, string division, string slotId)
    {
        // Similar logic - slot owner or admin can cancel
        var role = await GetUserRoleAsync(userId, leagueId);
        return !string.Equals(role, Constants.Roles.Viewer, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> GetUserRoleAsync(string userId, string leagueId)
    {
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
            return Constants.Roles.LeagueAdmin; // Treat global admin as league admin

        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        if (membership == null)
            return Constants.Roles.Viewer;

        return (membership.GetString("Role") ?? Constants.Roles.Viewer).Trim();
    }

    public async Task<(string division, string teamId)> GetCoachAssignmentAsync(string userId, string leagueId)
    {
        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        if (membership == null)
            return ("", "");

        var division = (membership.GetString("CoachDivision") ?? "").Trim();
        var teamId = (membership.GetString("CoachTeamId") ?? "").Trim();

        return (division, teamId);
    }
}
