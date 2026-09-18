using Azure.Data.Tables;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<(string userId, string leagueId), Lazy<Task<TableEntity?>>> _memberships = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _globalAdmins = new();

    public AuthorizationService(
        IMembershipRepository membershipRepo,
        ISlotRepository slotRepo,
        ILogger<AuthorizationService> logger)
    {
        _membershipRepo = membershipRepo;
        _slotRepo = slotRepo;
        _logger = logger;
    }

    /// <summary>
    /// Gets membership with request-scoped caching to avoid redundant queries.
    /// </summary>
    private async Task<TableEntity?> GetMembershipWithCacheAsync(
        string userId, string leagueId, CorrelationContext? context = null)
    {
        var lookup = _memberships.GetOrAdd((userId, leagueId), key =>
            new Lazy<Task<TableEntity?>>(() => _membershipRepo.GetMembershipAsync(key.userId, key.leagueId)));
        return await lookup.Value;
    }

    public async Task<string> GetUserRoleAsync(string userId, string leagueId)
    {
        // Check if global admin first
        if (string.IsNullOrWhiteSpace(userId) || userId == "UNKNOWN")
            return Constants.Roles.Viewer;
        var globalLookup = _globalAdmins.GetOrAdd(userId, key => new Lazy<Task<bool>>(() => _membershipRepo.IsGlobalAdminAsync(key)));
        if (await globalLookup.Value)
        {
            return Constants.Roles.LeagueAdmin;
        }

        // Get membership
        var membership = await GetMembershipWithCacheAsync(userId, leagueId);
        if (membership == null)
        {
            return Constants.Roles.Viewer; // No membership = viewer
        }

        return membership.GetString("Role") ?? Constants.Roles.Viewer;
    }

    public async Task ValidateNotViewerAsync(string userId, string leagueId)
    {
        var role = await GetUserRoleAsync(userId, leagueId);

        if (role != Constants.Roles.Coach && role != Constants.Roles.LeagueAdmin)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN, 
                "A coach or league administrator role is required for this action.");
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
        if (role != Constants.Roles.Coach)
        {
            throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                "A coach or league administrator role is required for this action.");
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
            var membership = await GetMembershipWithCacheAsync(userId, leagueId);
            if (membership == null)
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                    "No membership found");
            }

            var coachDivision = ReadMembershipDivision(membership);
            var coachTeamId = ReadMembershipTeamId(membership);

            if (coachDivision != division)
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.COACH_DIVISION_MISMATCH,
                    $"Coach is assigned to division '{coachDivision}', not '{division}'");
            }

            if (coachTeamId != teamId)
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
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
                var membership = await GetMembershipWithCacheAsync(userId, leagueId);
                if (membership == null)
                {
                    return false;
                }

                var coachTeamId = ReadMembershipTeamId(membership);

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

            return false;
        }
        catch (ApiGuards.HttpError)
        {
            return false;
        }
    }

    private static string ReadMembershipDivision(TableEntity? membership)
    {
        return (membership?.GetString("Division") ?? "").Trim();
    }

    private static string ReadMembershipTeamId(TableEntity? membership)
    {
        return (membership?.GetString("TeamId") ?? "").Trim();
    }
}
