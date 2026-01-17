using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Models;

/// <summary>
/// Email queue item stored in Azure Table Storage
/// PartitionKey: status (Pending, Sent, Failed)
/// RowKey: emailId (GUID)
/// </summary>
public class EmailQueueItem : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // status: Pending, Sent, Failed
    public string RowKey { get; set; } = default!;       // emailId (GUID)

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// Recipient email address
    /// </summary>
    public string To { get; set; } = default!;

    /// <summary>
    /// Email subject
    /// </summary>
    public string Subject { get; set; } = default!;

    /// <summary>
    /// Email body (HTML or plain text)
    /// </summary>
    public string Body { get; set; } = default!;

    /// <summary>
    /// Email type for categorization (SlotCreated, RequestApproved, etc.)
    /// </summary>
    public string? EmailType { get; set; }

    /// <summary>
    /// Related user ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Related league ID
    /// </summary>
    public string? LeagueId { get; set; }

    /// <summary>
    /// When the email was queued
    /// </summary>
    public DateTime QueuedUtc { get; set; }

    /// <summary>
    /// When the email was sent (if applicable)
    /// </summary>
    public DateTime? SentUtc { get; set; }

    /// <summary>
    /// Number of send attempts
    /// </summary>
    public int AttemptCount { get; set; } = 0;

    /// <summary>
    /// Last error message (if failed)
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// When to retry sending (if failed)
    /// </summary>
    public DateTime? RetryAfterUtc { get; set; }
}
