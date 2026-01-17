using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of IAuthorizationService for role-based access control.
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly IMembershipRepository _membershipRepo;
    private readonly ISlotRepository _slotRepo;
    private readonly ILogger<AuthorizationService> _logger;

    public AuthorizationService(
        IMembershipRepository membershipRepo,
        ISlotRepository slotRepo,
        ILogger<AuthorizationService> logger)
    {
        _membershipRepo = membershipRepo;
        _slotRepo = slotRepo;
        _logger = logger;
    }

    public async Task<string> GetUserRoleAsync(string userId, string leagueId)
    {
        // Check if global admin first
        if (await _membershipRepo.IsGlobalAdminAsync(userId))
        {
            return Constants.Roles.LeagueAdmin;
        }

        // Get membership
        var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
        if (membership == null)
        {
            return Constants.Roles.Viewer; // No membership = viewer
        }

        return membership.GetString("Role") ?? Constants.Roles.Viewer;
    }

    public async Task ValidateNotViewerAsync(string userId, string leagueId)
    {
        var role = await GetUserRoleAsync(userId, leagueId);

        if (role == Constants.Roles.Viewer)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN, 
                "Viewers cannot modify content. Contact an admin for access.");
        }
    }

    public async Task ValidateCoachAccessAsync(string userId, string leagueId, string division, string? teamId)
    {
        var role = await GetUserRoleAsync(userId, leagueId);

        // Admins can do anything
        if (role == Constants.Roles.LeagueAdmin)
        {
            return;
        }

        // Viewers cannot modify
        if (role == Constants.Roles.Viewer)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                "Viewers cannot modify content. Contact an admin for access.");
        }

        // Coaches must provide a team
        if (role == Constants.Roles.Coach)
        {
            if (string.IsNullOrWhiteSpace(teamId))
            {
                throw new ApiGuards.HttpError(400, ErrorCodes.COACH_TEAM_REQUIRED,
                    "Coaches must specify a team ID");
            }

            // Validate coach is assigned to this division and team
            var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
            if (membership == null)
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.UNAUTHORIZED,
                    "No membership found");
            }

            var coachDivision = membership.GetString("CoachDivision") ?? "";
            var coachTeamId = membership.GetString("CoachTeamId") ?? "";

            if (coachDivision != division)
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.COACH_DIVISION_MISMATCH,
                    $"Coach is assigned to division '{coachDivision}', not '{division}'");
            }

            if (coachTeamId != teamId)
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.UNAUTHORIZED,
                    $"Coach is assigned to team '{coachTeamId}', not '{teamId}'");
            }
        }
    }

    public async Task<bool> CanCreateSlotAsync(string userId, string leagueId, string division, string? teamId)
    {
        try
        {
            await ValidateNotViewerAsync(userId, leagueId);

            var role = await GetUserRoleAsync(userId, leagueId);
            if (role == Constants.Roles.Coach)
            {
                await ValidateCoachAccessAsync(userId, leagueId, division, teamId);
            }

            return true;
        }
        catch (ApiGuards.HttpError)
        {
            return false;
        }
    }

    public async Task<bool> CanApproveRequestAsync(string userId, string leagueId, string division, string slotId)
    {
        try
        {
            await ValidateNotViewerAsync(userId, leagueId);

            var role = await GetUserRoleAsync(userId, leagueId);

            // Admins can approve any request
            if (role == Constants.Roles.LeagueAdmin)
            {
                return true;
            }

            // Coaches can only approve requests for their own slots
            if (role == Constants.Roles.Coach)
            {
                var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
                if (slot == null)
                {
                    return false;
                }

                var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
                if (membership == null)
                {
                    return false;
                }

                var coachTeamId = membership.GetString("CoachTeamId") ?? "";
                var offeringTeamId = slot.GetString("OfferingTeamId") ?? "";

                // Coach must own the slot being requested
                return coachTeamId == offeringTeamId;
            }

            return false;
        }
        catch (ApiGuards.HttpError)
        {
            return false;
        }
    }

    public async Task<bool> CanCancelSlotAsync(string userId, string leagueId, string offeringTeamId, string? confirmedTeamId)
    {
        try
        {
            await ValidateNotViewerAsync(userId, leagueId);

            var role = await GetUserRoleAsync(userId, leagueId);

            // Admins can cancel any slot
            if (role == Constants.Roles.LeagueAdmin)
            {
                return true;
            }

            // Coaches can cancel their own slots
            if (role == Constants.Roles.Coach)
            {
                var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
                if (membership == null)
                {
                    return false;
                }

                var coachTeamId = membership.GetString("CoachTeamId") ?? "";

                // Coach must own the slot (either offering or confirmed team)
                return coachTeamId == offeringTeamId || coachTeamId == confirmedTeamId;
            }

            return false;
        }
        catch (ApiGuards.HttpError)
        {
            return false;
        }
    }

    public async Task<bool> CanUpdateSlotAsync(string userId, string leagueId, string division, string slotId)
    {
        try
        {
            await ValidateNotViewerAsync(userId, leagueId);

            var role = await GetUserRoleAsync(userId, leagueId);

            // Admins can update any slot
            if (role == Constants.Roles.LeagueAdmin)
            {
                return true;
            }

            // Coaches can only update their own slots
            if (role == Constants.Roles.Coach)
            {
                var slot = await _slotRepo.GetSlotAsync(leagueId, division, slotId);
                if (slot == null)
                {
                    return false;
                }

                var membership = await _membershipRepo.GetMembershipAsync(userId, leagueId);
                if (membership == null)
                {
                    return false;
                }

                var coachTeamId = membership.GetString("CoachTeamId") ?? "";
                var offeringTeamId = slot.GetString("OfferingTeamId") ?? "";

                // Coach must own the slot
                return coachTeamId == offeringTeamId;
            }

            return false;
        }
        catch (ApiGuards.HttpError)
        {
            return false;
        }
    }
}
