using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of IUmpireService for umpire profile management.
/// </summary>
public class UmpireService : IUmpireService
{
    private readonly IUmpireProfileRepository _umpireRepo;
    private readonly IGameUmpireAssignmentRepository _assignmentRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly INotificationService _notificationService;
    private readonly ILogger<UmpireService> _logger;

    public UmpireService(
        IUmpireProfileRepository umpireRepo,
        IGameUmpireAssignmentRepository assignmentRepo,
        IMembershipRepository membershipRepo,
        INotificationService notificationService,
        ILogger<UmpireService> logger)
    {
        _umpireRepo = umpireRepo;
        _assignmentRepo = assignmentRepo;
        _membershipRepo = membershipRepo;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<object> CreateUmpireAsync(CreateUmpireRequest request, CorrelationContext context)
    {
        // Authorization: LeagueAdmin required
        var isGlobalAdmin = await _membershipRepo.IsGlobalAdminAsync(context.UserId);
        if (!isGlobalAdmin)
        {
            var membership = await _membershipRepo.GetMembershipAsync(context.UserId, context.LeagueId);
            var role = (membership?.GetString("Role") ?? "").Trim();
            if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                    "Only league admins can create umpire profiles");
            }
        }

        // Validation
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "Name is required");

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "Email is required");

        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new ApiGuards.HttpError(400, ErrorCodes.MISSING_REQUIRED_FIELD, "Phone is required");

        // Generate umpire user ID (GUID for new umpires, or could link to existing user)
        var umpireUserId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        // Create umpire entity
        var pk = $"UMPIRE|{context.LeagueId}";
        var umpire = new TableEntity(pk, umpireUserId)
        {
            ["LeagueId"] = context.LeagueId,
            ["UmpireUserId"] = umpireUserId,
            ["Name"] = request.Name.Trim(),
            ["Email"] = request.Email.Trim().ToLowerInvariant(),
            ["Phone"] = request.Phone.Trim(),
            ["CertificationLevel"] = request.CertificationLevel?.Trim() ?? "",
            ["YearsExperience"] = request.YearsExperience ?? 0,
            ["Notes"] = request.Notes?.Trim() ?? "",
            ["IsActive"] = true,
            ["CreatedUtc"] = now,
            ["UpdatedUtc"] = now,
            ["CreatedBy"] = context.UserId,
            ["UpdatedBy"] = context.UserId
        };

        await _umpireRepo.CreateUmpireAsync(umpire);

        _logger.LogInformation("Created umpire profile {UmpireUserId} in league {LeagueId}", umpireUserId, context.LeagueId);

        return MapUmpireToDto(umpire);
    }

    public async Task<object?> GetUmpireAsync(string leagueId, string umpireUserId)
    {
        var umpire = await _umpireRepo.GetUmpireAsync(leagueId, umpireUserId);
        if (umpire == null)
            return null;

        return MapUmpireToDto(umpire);
    }

    public async Task<List<object>> QueryUmpiresAsync(string leagueId, UmpireQueryFilter filter)
    {
        List<TableEntity> umpires;

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            umpires = await _umpireRepo.SearchUmpiresByNameAsync(leagueId, filter.SearchTerm);
        }
        else
        {
            umpires = await _umpireRepo.QueryUmpiresAsync(leagueId, filter.ActiveOnly);
        }

        return umpires.Select(MapUmpireToDto).ToList();
    }

    public async Task<object> UpdateUmpireAsync(string umpireUserId, UpdateUmpireRequest request, CorrelationContext context)
    {
        var umpire = await _umpireRepo.GetUmpireAsync(context.LeagueId, umpireUserId);
        if (umpire == null)
            throw new ApiGuards.HttpError(404, ErrorCodes.UMPIRE_NOT_FOUND, "Umpire not found");

        // Authorization: LeagueAdmin OR self (umpire updating own profile)
        var isSelf = string.Equals(context.UserId, umpireUserId, StringComparison.OrdinalIgnoreCase);
        var isGlobalAdmin = await _membershipRepo.IsGlobalAdminAsync(context.UserId);
        var isLeagueAdmin = false;

        if (!isSelf && !isGlobalAdmin)
        {
            var membership = await _membershipRepo.GetMembershipAsync(context.UserId, context.LeagueId);
            var role = (membership?.GetString("Role") ?? "").Trim();
            isLeagueAdmin = string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase);

            if (!isLeagueAdmin)
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                    "Only league admins or the umpire themselves can update this profile");
            }
        }

        // Update fields (only non-null values)
        if (!string.IsNullOrWhiteSpace(request.Name))
            umpire["Name"] = request.Name.Trim();

        if (!string.IsNullOrWhiteSpace(request.Phone))
            umpire["Phone"] = request.Phone.Trim();

        // Admin-only fields (umpires can't update these themselves)
        if (isGlobalAdmin || isLeagueAdmin)
        {
            if (!string.IsNullOrWhiteSpace(request.CertificationLevel))
                umpire["CertificationLevel"] = request.CertificationLevel.Trim();

            if (request.YearsExperience.HasValue)
                umpire["YearsExperience"] = request.YearsExperience.Value;

            if (request.Notes != null)
                umpire["Notes"] = request.Notes.Trim();
        }

        // Photo URL (both admin and self can update)
        if (request.PhotoUrl != null)
            umpire["PhotoUrl"] = request.PhotoUrl.Trim();

        umpire["UpdatedUtc"] = DateTime.UtcNow;
        umpire["UpdatedBy"] = context.UserId;

        await _umpireRepo.UpdateUmpireAsync(umpire, umpire.ETag);

        _logger.LogInformation("Updated umpire profile {UmpireUserId}", umpireUserId);

        return MapUmpireToDto(umpire);
    }

    public async Task DeactivateUmpireAsync(string umpireUserId, bool reassignFutureGames, CorrelationContext context)
    {
        // Authorization: LeagueAdmin only
        var isGlobalAdmin = await _membershipRepo.IsGlobalAdminAsync(context.UserId);
        if (!isGlobalAdmin)
        {
            var membership = await _membershipRepo.GetMembershipAsync(context.UserId, context.LeagueId);
            var role = (membership?.GetString("Role") ?? "").Trim();
            if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError(403, ErrorCodes.FORBIDDEN,
                    "Only league admins can deactivate umpires");
            }
        }

        var umpire = await _umpireRepo.GetUmpireAsync(context.LeagueId, umpireUserId);
        if (umpire == null)
            throw new ApiGuards.HttpError(404, ErrorCodes.UMPIRE_NOT_FOUND, "Umpire not found");

        // Set inactive
        umpire["IsActive"] = false;
        umpire["UpdatedUtc"] = DateTime.UtcNow;
        umpire["UpdatedBy"] = context.UserId;

        await _umpireRepo.UpdateUmpireAsync(umpire, umpire.ETag);

        _logger.LogInformation("Deactivated umpire {UmpireUserId}", umpireUserId);

        // Reassign future games if requested
        if (reassignFutureGames)
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var futureAssignments = await _assignmentRepo.GetAssignmentsByUmpireAsync(
                context.LeagueId, umpireUserId, dateFrom: today);

            var cancelledCount = 0;
            foreach (var assignment in futureAssignments)
            {
                var status = (assignment.GetString("Status") ?? "").Trim();
                if (status == "Cancelled") continue;

                assignment["Status"] = "Cancelled";
                assignment["DeclineReason"] = "Umpire deactivated by admin";
                assignment["UpdatedUtc"] = DateTime.UtcNow;

                await _assignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);
                cancelledCount++;
            }

            _logger.LogInformation("Cancelled {Count} future assignments for deactivated umpire {UmpireUserId}",
                cancelledCount, umpireUserId);

            // Notify admin
            await _notificationService.CreateNotificationAsync(
                context.UserId,
                context.LeagueId,
                "UmpireDeactivated",
                $"{umpire.GetString("Name")} deactivated. {cancelledCount} future games returned to unassigned queue.",
                "#admin",
                umpireUserId,
                "UmpireManagement");
        }
    }

    private static object MapUmpireToDto(TableEntity entity)
    {
        return new
        {
            umpireUserId = entity.RowKey,
            leagueId = entity.GetString("LeagueId") ?? "",
            name = entity.GetString("Name") ?? "",
            email = entity.GetString("Email") ?? "",
            phone = entity.GetString("Phone") ?? "",
            photoUrl = entity.GetString("PhotoUrl"),
            certificationLevel = entity.GetString("CertificationLevel"),
            yearsExperience = entity.GetInt32("YearsExperience"),
            notes = entity.GetString("Notes"),
            isActive = entity.GetBoolean("IsActive") ?? true,
            createdUtc = entity.GetDateTime("CreatedUtc"),
            updatedUtc = entity.GetDateTime("UpdatedUtc")
        };
    }
}
