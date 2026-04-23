using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Models;

/// <summary>
/// Game umpire assignment entity.
/// Links an umpire to a specific game with status tracking.
/// PartitionKey: UMPASSIGN|{leagueId}|{division}|{slotId}
/// RowKey: {assignmentId}
/// </summary>
public class GameUmpireAssignment : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // UMPASSIGN|{leagueId}|{division}|{slotId}
    public string RowKey { get; set; } = default!;       // assignmentId (GUID)

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// League identifier
    /// </summary>
    public string LeagueId { get; set; } = default!;

    /// <summary>
    /// Division code
    /// </summary>
    public string Division { get; set; } = default!;

    /// <summary>
    /// Game slot ID (foreign key to GameSwapSlots)
    /// </summary>
    public string SlotId { get; set; } = default!;

    /// <summary>
    /// Unique assignment identifier
    /// </summary>
    public string AssignmentId { get; set; } = default!;

    /// <summary>
    /// Assigned umpire user ID
    /// </summary>
    public string UmpireUserId { get; set; } = default!;

    /// <summary>
    /// Position/role for this assignment (e.g., "Home Plate", "Field", "Base")
    /// Used for multi-umpire games (Phase 2)
    /// NULL for single-umpire games (MVP)
    /// </summary>
    public string? Position { get; set; }

    /// <summary>
    /// Assignment status:
    /// - "Assigned" = Admin assigned, umpire hasn't responded yet
    /// - "Accepted" = Umpire confirmed they will officiate
    /// - "Declined" = Umpire cannot make it
    /// - "Cancelled" = Assignment removed (game cancelled or umpire unassigned)
    /// </summary>
    public string Status { get; set; } = "Assigned";

    // ============================================
    // DENORMALIZED GAME DETAILS
    // Stored here to enable fast umpire-scoped queries
    // without joining to GameSwapSlots table
    // Updated when game details change
    // ============================================

    /// <summary>
    /// Game date (YYYY-MM-DD)
    /// </summary>
    public string GameDate { get; set; } = default!;

    /// <summary>
    /// Game start time (HH:MM)
    /// </summary>
    public string StartTime { get; set; } = default!;

    /// <summary>
    /// Game end time (HH:MM)
    /// </summary>
    public string EndTime { get; set; } = default!;

    /// <summary>
    /// Start time in minutes from midnight (for conflict detection)
    /// </summary>
    public int StartMin { get; set; }

    /// <summary>
    /// End time in minutes from midnight (for conflict detection)
    /// </summary>
    public int EndMin { get; set; }

    /// <summary>
    /// Field key (park/field identifier)
    /// </summary>
    public string FieldKey { get; set; } = default!;

    /// <summary>
    /// Field display name (user-friendly field name)
    /// </summary>
    public string? FieldDisplayName { get; set; }

    /// <summary>
    /// Home team ID
    /// </summary>
    public string? HomeTeamId { get; set; }

    /// <summary>
    /// Away team ID
    /// </summary>
    public string? AwayTeamId { get; set; }

    // ============================================
    // ASSIGNMENT METADATA
    // ============================================

    /// <summary>
    /// Admin who made the assignment
    /// </summary>
    public string AssignedBy { get; set; } = default!;

    /// <summary>
    /// When the assignment was created
    /// </summary>
    public DateTime AssignedUtc { get; set; }

    /// <summary>
    /// When the umpire responded (accepted or declined)
    /// NULL if still pending
    /// </summary>
    public DateTime? ResponseUtc { get; set; }

    /// <summary>
    /// Reason provided if umpire declined
    /// </summary>
    public string? DeclineReason { get; set; }

    /// <summary>
    /// Whether admin flagged this umpire as a no-show
    /// </summary>
    public bool NoShowFlagged { get; set; } = false;

    /// <summary>
    /// Admin notes about the no-show
    /// </summary>
    public string? NoShowNotes { get; set; }

    /// <summary>
    /// When the no-show was flagged
    /// </summary>
    public DateTime? NoShowFlaggedUtc { get; set; }

    /// <summary>
    /// When the assignment record was created
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// When the assignment was last updated
    /// </summary>
    public DateTime UpdatedUtc { get; set; }
}
