using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service interface for umpire profile management operations.
/// </summary>
public interface IUmpireService
{
    /// <summary>
    /// Creates a new umpire profile.
    /// </summary>
    /// <param name="request">Umpire creation request</param>
    /// <param name="context">Request correlation context</param>
    /// <returns>Created umpire profile DTO</returns>
    Task<object> CreateUmpireAsync(CreateUmpireRequest request, CorrelationContext context);

    /// <summary>
    /// Gets a single umpire profile by user ID.
    /// </summary>
    Task<object?> GetUmpireAsync(string leagueId, string umpireUserId);

    /// <summary>
    /// Queries all umpires in a league with optional filtering.
    /// </summary>
    Task<List<object>> QueryUmpiresAsync(string leagueId, UmpireQueryFilter filter);

    /// <summary>
    /// Updates an existing umpire profile.
    /// Authorization: LeagueAdmin OR self (umpire can update limited fields).
    /// </summary>
    Task<object> UpdateUmpireAsync(string umpireUserId, UpdateUmpireRequest request, CorrelationContext context);

    /// <summary>
    /// Deactivates an umpire (soft delete).
    /// If reassignFutureGames is true, all future assignments are cancelled and games returned to unassigned.
    /// </summary>
    Task DeactivateUmpireAsync(string umpireUserId, bool reassignFutureGames, CorrelationContext context);
}

/// <summary>
/// Request model for creating an umpire profile.
/// </summary>
public class CreateUmpireRequest
{
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Phone { get; set; } = default!;
    public string? CertificationLevel { get; set; }
    public int? YearsExperience { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Request model for updating an umpire profile.
/// </summary>
public class UpdateUmpireRequest
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? CertificationLevel { get; set; }
    public int? YearsExperience { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
}

/// <summary>
/// Filter for querying umpires.
/// </summary>
public class UmpireQueryFilter
{
    public bool? ActiveOnly { get; set; }
    public string? SearchTerm { get; set; }
}
