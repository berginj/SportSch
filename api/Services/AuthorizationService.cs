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

    public async Task<bool> CanApproveRequestAsync(string userId, string leagueId, string division, string offeringTeamId)
    {
        // Global admins can approve any request
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
            return true;

        // Get user's membership
        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        if (membership == null)
            return false;

        var role = (membership.GetString("Role") ?? Constants.Roles.Viewer).Trim();

        // League admins can approve any request
        if (string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            return true;

        // Coaches can only approve requests for their own offered slots
        if (string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
        {
            var coachDivision = (membership.GetString("CoachDivision") ?? "").Trim();
            var coachTeamId = (membership.GetString("CoachTeamId") ?? "").Trim();

            if (string.IsNullOrWhiteSpace(coachTeamId))
            {
                _logger.LogWarning("Coach {UserId} has no assigned team", userId);
                return false;
            }

            // Check division match
            if (!string.IsNullOrWhiteSpace(coachDivision) &&
                !string.Equals(coachDivision, division, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Coach {UserId} attempted to approve request in different division", userId);
                return false;
            }

            // Check team match (must be offering coach)
            if (!string.Equals(coachTeamId, offeringTeamId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Coach {UserId} attempted to approve request for different team's slot", userId);
                return false;
            }

            return true;
        }

        // Viewers cannot approve requests
        return false;
    }

    public async Task<bool> CanCancelSlotAsync(string userId, string leagueId, string offeringTeamId, string? confirmedTeamId)
    {
        // Global admins can cancel any slot
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
            return true;

        // Get user's membership
        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        if (membership == null)
            return false;

        var role = (membership.GetString("Role") ?? Constants.Roles.Viewer).Trim();

        // League admins can cancel any slot
        if (string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            return true;

        // Coaches can cancel slots for their team (either offering or confirmed)
        if (string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
        {
            var myTeamId = (membership.GetString("CoachTeamId") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(myTeamId))
                return false;

            // Can cancel if coach's team is either offering or confirmed
            if (string.Equals(myTeamId, offeringTeamId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(confirmedTeamId) &&
                string.Equals(myTeamId, confirmedTeamId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
