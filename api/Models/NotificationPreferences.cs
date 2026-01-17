using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Models;

/// <summary>
/// User notification preferences stored in Azure Table Storage
/// PartitionKey: userId
/// RowKey: leagueId (or "global" for global preferences)
/// </summary>
public class NotificationPreferences : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // userId
    public string RowKey { get; set; } = default!;       // leagueId or "global"

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // In-app notification preferences
    public bool EnableInAppNotifications { get; set; } = true;

    // Email notification preferences
    public bool EnableEmailNotifications { get; set; } = true;
    public bool EmailOnSlotCreated { get; set; } = true;
    public bool EmailOnSlotCancelled { get; set; } = true;
    public bool EmailOnRequestReceived { get; set; } = true;
    public bool EmailOnRequestApproved { get; set; } = true;
    public bool EmailOnRequestDenied { get; set; } = true;
    public bool EmailOnGameReminder { get; set; } = true;

    // Digest settings
    public bool EnableDailyDigest { get; set; } = false;
    public string? DigestTime { get; set; } // HH:mm in user's timezone

    /// <summary>
    /// When preferences were last updated
    /// </summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// User's email address (for convenience)
    /// </summary>
    public string? Email { get; set; }
}
