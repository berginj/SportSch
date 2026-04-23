using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Models;

/// <summary>
/// Umpire availability and blackout rule entity.
/// PartitionKey: UMPAVAIL|{leagueId}|{umpireUserId}
/// RowKey: {ruleId}
/// </summary>
public class UmpireAvailability : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // UMPAVAIL|{leagueId}|{umpireUserId}
    public string RowKey { get; set; } = default!;       // ruleId (GUID)

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// League identifier
    /// </summary>
    public string LeagueId { get; set; } = default!;

    /// <summary>
    /// Umpire user ID
    /// </summary>
    public string UmpireUserId { get; set; } = default!;

    /// <summary>
    /// Unique rule identifier
    /// </summary>
    public string RuleId { get; set; } = default!;

    /// <summary>
    /// Rule type: "Availability" (when umpire IS available) or "Blackout" (when umpire is NOT available)
    /// Blackouts override Availability rules
    /// </summary>
    public string RuleType { get; set; } = default!;

    /// <summary>
    /// Start date of availability window (YYYY-MM-DD)
    /// </summary>
    public string DateFrom { get; set; } = default!;

    /// <summary>
    /// End date of availability window (YYYY-MM-DD, inclusive)
    /// </summary>
    public string DateTo { get; set; } = default!;

    /// <summary>
    /// Days of week when rule applies (comma-separated: "Mon,Wed,Fri")
    /// NULL = all days of the week
    /// Phase 2 feature - MVP uses null (all days)
    /// </summary>
    public string? DaysOfWeek { get; set; }

    /// <summary>
    /// Start time of availability window (HH:MM in 24-hour format)
    /// NULL = start of day (all day)
    /// Phase 2 feature - MVP uses null (all day)
    /// </summary>
    public string? StartTime { get; set; }

    /// <summary>
    /// End time of availability window (HH:MM in 24-hour format)
    /// NULL = end of day (all day)
    /// Phase 2 feature - MVP uses null (all day)
    /// </summary>
    public string? EndTime { get; set; }

    /// <summary>
    /// User-provided reason for this rule (especially useful for blackouts)
    /// Example: "Vacation", "Work commitment", "Out of town"
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// When the rule was created
    /// </summary>
    public DateTime CreatedUtc { get; set; }
}
