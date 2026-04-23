using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Models;

/// <summary>
/// Umpire/Official profile entity.
/// PartitionKey: UMPIRE|{leagueId}
/// RowKey: {umpireUserId}
/// </summary>
public class UmpireProfile : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // UMPIRE|{leagueId}
    public string RowKey { get; set; } = default!;       // umpireUserId

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// League identifier
    /// </summary>
    public string LeagueId { get; set; } = default!;

    /// <summary>
    /// User ID linking to Azure SWA identity
    /// </summary>
    public string UmpireUserId { get; set; } = default!;

    /// <summary>
    /// Full name of the umpire
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Contact email address
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Contact phone number (E.164 format recommended)
    /// </summary>
    public string Phone { get; set; } = default!;

    /// <summary>
    /// Profile photo URL (optional, stored in blob storage)
    /// Phase 2 feature
    /// </summary>
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Certification level (e.g., "Level 1", "Level 2", "Certified")
    /// </summary>
    public string? CertificationLevel { get; set; }

    /// <summary>
    /// Years of officiating experience
    /// </summary>
    public int? YearsExperience { get; set; }

    /// <summary>
    /// Admin notes about this umpire
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Preferred fields (Phase 2 feature)
    /// Stored as comma-separated field keys
    /// </summary>
    public string? PreferredFields { get; set; }

    /// <summary>
    /// Maximum games per day (soft limit, Phase 2)
    /// </summary>
    public int? MaxGamesPerDay { get; set; }

    /// <summary>
    /// Maximum games per week (soft limit, Phase 2)
    /// </summary>
    public int? MaxGamesPerWeek { get; set; }

    /// <summary>
    /// Travel radius in miles (Phase 2 feature)
    /// </summary>
    public int? TravelRadiusMiles { get; set; }

    /// <summary>
    /// Whether the umpire is currently active
    /// Inactive umpires cannot be assigned to new games
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When the profile was created
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// When the profile was last updated
    /// </summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// Who created this profile (admin userId)
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Who last updated this profile (admin or self)
    /// </summary>
    public string? UpdatedBy { get; set; }
}
