using Azure;
using Azure.Data.Tables;

public class GameSlotEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // Division
    public string RowKey { get; set; } = default!;       // SlotId (GUID)

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string OfferingTeamId { get; set; } = default!;
    public string GameDate { get; set; } = default!;     // yyyy-MM-dd
    public string StartTime { get; set; } = default!;    // HH:mm
    public string EndTime { get; set; } = default!;
    public string Field { get; set; } = default!;
    public string GameType { get; set; } = default!;
    public string Status { get; set; } = "Open";
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}
