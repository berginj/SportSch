using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Models;

/// <summary>
/// Notification entity stored in Azure Table Storage
/// PartitionKey: userId
/// RowKey: notificationId (GUID)
/// </summary>
public class Notification : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // userId
    public string RowKey { get; set; } = default!;       // notificationId (GUID)

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// Type of notification: SlotCreated, SlotCancelled, RequestReceived, RequestApproved, RequestDenied, etc.
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>
    /// Notification message text
    /// </summary>
    public string Message { get; set; } = default!;

    /// <summary>
    /// Link/route to navigate to when clicking notification (e.g., #calendar, #offers)
    /// </summary>
    public string? Link { get; set; }

    /// <summary>
    /// Related entity ID (e.g., slotId, requestId)
    /// </summary>
    public string? RelatedEntityId { get; set; }

    /// <summary>
    /// Related entity type (e.g., Slot, Request)
    /// </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// League context for this notification
    /// </summary>
    public string LeagueId { get; set; } = default!;

    /// <summary>
    /// Whether the notification has been read
    /// </summary>
    public bool IsRead { get; set; } = false;

    /// <summary>
    /// When the notification was created
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// When the notification was read (if applicable)
    /// </summary>
    public DateTime? ReadUtc { get; set; }
}
