using System.Text.Json.Serialization;

namespace GameSwap.Functions.Models;

public static class FieldInventoryImportStatuses
{
    public const string Draft = "draft";
    public const string Parsed = "parsed";
    public const string Staged = "staged";
    public const string Imported = "imported";
    public const string Errored = "errored";
}

public static class FieldInventoryParserTypes
{
    public const string SeasonWeekdayGrid = "season_weekday_grid";
    public const string WeekendGrid = "weekend_grid";
    public const string ReferenceGrid = "reference_grid";
    public const string Ignore = "ignore";
}

public static class FieldInventoryActionTypes
{
    public const string Ingest = "ingest";
    public const string Reference = "reference";
    public const string Ignore = "ignore";
}

public static class FieldInventoryAvailabilityStatuses
{
    public const string Available = "available";
    public const string Unavailable = "unavailable";
    public const string Pending = "pending";
    public const string Unknown = "unknown";
}

public static class FieldInventoryUtilizationStatuses
{
    public const string Used = "used";
    public const string NotUsed = "not_used";
    public const string Unknown = "unknown";
}

public static class FieldInventoryReviewStatuses
{
    public const string None = "none";
    public const string NeedsReview = "needs_review";
    public const string Resolved = "resolved";
}

public static class FieldInventoryConfidence
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public static class FieldInventoryReviewItemTypes
{
    public const string FieldMapping = "field_mapping";
    public const string TabClassification = "tab_classification";
    public const string AmbiguousParse = "ambiguous_parse";
    public const string LowConfidence = "low_confidence";
    public const string Other = "other";
}

public static class FieldInventoryReviewItemSeverities
{
    public const string Blocking = "blocking";
    public const string NonBlocking = "non_blocking";
}

public static class FieldInventoryWarningSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
}

public static class FieldInventoryReviewItemStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Ignored = "ignored";
}

public static class FieldInventoryCommitModes
{
    public const string Import = "import";
    public const string Upsert = "upsert";
}

public record FieldInventoryWorkbookInspectRequest(string? SourceWorkbookUrl);

public record FieldInventorySelectedTab(
    string? TabName,
    string? ParserType,
    string? ActionType,
    bool Selected = true);

public record FieldInventoryPreviewRequest(
    string? SourceWorkbookUrl,
    string? SeasonLabel,
    List<FieldInventorySelectedTab>? SelectedTabs);

public record FieldInventoryAliasSaveRequest(
    string? RawFieldName,
    string? CanonicalFieldId,
    string? CanonicalFieldName,
    string? RunId,
    bool SaveForFuture = true);

public record FieldInventoryTabClassificationSaveRequest(
    string? RawTabName,
    string? ParserType,
    string? ActionType,
    string? WorkbookTitlePattern,
    string? RunId,
    bool SaveForFuture = true);

public record FieldInventoryReviewDecisionRequest(
    string? Status,
    Dictionary<string, string?>? ChosenResolution,
    bool SaveDecisionForFuture = false);

public record FieldInventoryCommitRequest(
    string? Mode,
    bool DryRun = true,
    bool ReplaceExistingSeason = true);

public record FieldInventoryWorkbookInspectResponse(
    string SourceWorkbookUrl,
    string SpreadsheetId,
    string SourceWorkbookTitle,
    List<FieldInventoryWorkbookTabDto> Tabs);

public record FieldInventoryWorkbookTabDto(
    string TabName,
    int Index,
    string InferredParserType,
    string InferredActionType,
    string Confidence,
    string Reason,
    int NonEmptyCellCount,
    int MergedRangeCount);

public record FieldInventorySummaryCountsDto(
    int ParsedRecords,
    int Warnings,
    int ReviewItems,
    int UnmappedFields,
    int SelectedTabs,
    int ImportedRecords,
    int SkippedRecords);

public record FieldInventoryRunDto(
    string Id,
    string SourceWorkbookUrl,
    string SourceWorkbookTitle,
    string SeasonLabel,
    string Status,
    List<FieldInventorySelectedTab> SelectedTabs,
    FieldInventorySummaryCountsDto SummaryCounts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedBy);

public record FieldInventoryPreviewResponse(
    FieldInventoryRunDto Run,
    List<FieldInventoryStagedRecordDto> Records,
    List<FieldInventoryWarningDto> Warnings,
    List<FieldInventoryReviewItemDto> ReviewItems,
    List<CanonicalFieldOptionDto> CanonicalFields,
    List<string> UnmappedFieldNames,
    FieldInventoryCommitPreviewDto? CommitPreview);

public record FieldInventoryStagedRecordDto(
    string Id,
    string ImportRunId,
    string? FieldId,
    string? FieldName,
    string RawFieldName,
    string Date,
    string DayOfWeek,
    string StartTime,
    string EndTime,
    int SlotDurationMinutes,
    string AvailabilityStatus,
    string UtilizationStatus,
    string? UsageType,
    string? UsedBy,
    string? AssignedGroup,
    string? AssignedDivision,
    string? AssignedTeamOrEvent,
    string SourceWorkbookUrl,
    string SourceTab,
    string SourceCellRange,
    string SourceValue,
    string? SourceColor,
    string ParserType,
    string Confidence,
    List<string> WarningFlags,
    string ReviewStatus);

public record FieldInventoryWarningDto(
    string Id,
    string ImportRunId,
    string Severity,
    string Code,
    string Message,
    string SourceTab,
    string SourceCellRange,
    string? RelatedRecordId);

public record FieldInventoryReviewItemDto(
    string Id,
    string ImportRunId,
    string ItemType,
    string Severity,
    string Title,
    string Description,
    string SourceTab,
    string SourceCellRange,
    string RawValue,
    Dictionary<string, string?> SuggestedResolution,
    Dictionary<string, string?> ChosenResolution,
    string Status,
    bool SaveDecisionForFuture);

public record FieldInventoryCommitPreviewDto(
    string Mode,
    bool DryRun,
    int CreateCount,
    int UpdateCount,
    int DeleteCount,
    int UnchangedCount,
    int SkippedUnmappedCount,
    string SeasonLabel);

public record CanonicalFieldOptionDto(
    string FieldId,
    string CanonicalFieldName,
    string FieldName,
    string ParkName);

public class FieldInventoryImportRunEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string SourceWorkbookUrl { get; set; } = "";
    public string SourceWorkbookTitle { get; set; } = "";
    public string SeasonLabel { get; set; } = "";
    public List<FieldInventorySelectedTab> SelectedTabs { get; set; } = new();
    public string Status { get; set; } = FieldInventoryImportStatuses.Draft;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public FieldInventorySummaryCountsDto SummaryCounts { get; set; } = new(0, 0, 0, 0, 0, 0, 0);
}

public class FieldInventoryStagedRecordEntity
{
    public string Id { get; set; } = "";
    public string ImportRunId { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string? FieldId { get; set; }
    public string? FieldName { get; set; }
    public string RawFieldName { get; set; } = "";
    public string Date { get; set; } = "";
    public string DayOfWeek { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public int SlotDurationMinutes { get; set; }
    public string AvailabilityStatus { get; set; } = FieldInventoryAvailabilityStatuses.Unknown;
    public string UtilizationStatus { get; set; } = FieldInventoryUtilizationStatuses.Unknown;
    public string? UsageType { get; set; }
    public string? UsedBy { get; set; }
    public string? AssignedGroup { get; set; }
    public string? AssignedDivision { get; set; }
    public string? AssignedTeamOrEvent { get; set; }
    public string SourceWorkbookUrl { get; set; } = "";
    public string SourceTab { get; set; } = "";
    public string SourceCellRange { get; set; } = "";
    public string SourceValue { get; set; } = "";
    public string? SourceColor { get; set; }
    public string ParserType { get; set; } = "";
    public string Confidence { get; set; } = FieldInventoryConfidence.Low;
    public List<string> WarningFlags { get; set; } = new();
    public string ReviewStatus { get; set; } = FieldInventoryReviewStatuses.None;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class FieldInventoryFieldAliasEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string RawFieldName { get; set; } = "";
    public string NormalizedLookupKey { get; set; } = "";
    public string CanonicalFieldId { get; set; } = "";
    public string CanonicalFieldName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public class FieldInventoryTabClassificationEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string WorkbookTitlePattern { get; set; } = "";
    public string RawTabName { get; set; } = "";
    public string ParserType { get; set; } = "";
    public string ActionType { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class FieldInventoryWarningEntity
{
    public string Id { get; set; } = "";
    public string ImportRunId { get; set; } = "";
    public string Severity { get; set; } = FieldInventoryWarningSeverities.Warning;
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string SourceTab { get; set; } = "";
    public string SourceCellRange { get; set; } = "";
    public string? RelatedRecordId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class FieldInventoryReviewQueueItemEntity
{
    public string Id { get; set; } = "";
    public string ImportRunId { get; set; } = "";
    public string ItemType { get; set; } = FieldInventoryReviewItemTypes.Other;
    public string Severity { get; set; } = FieldInventoryReviewItemSeverities.NonBlocking;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceTab { get; set; } = "";
    public string SourceCellRange { get; set; } = "";
    public string RawValue { get; set; } = "";
    public Dictionary<string, string?> SuggestedResolution { get; set; } = new();
    public Dictionary<string, string?> ChosenResolution { get; set; } = new();
    public string Status { get; set; } = FieldInventoryReviewItemStatuses.Open;
    public bool SaveDecisionForFuture { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class FieldInventoryLiveRecordEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string SeasonLabel { get; set; } = "";
    public string ImportRunId { get; set; } = "";
    public string FieldId { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string RawFieldName { get; set; } = "";
    public string Date { get; set; } = "";
    public string DayOfWeek { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public int SlotDurationMinutes { get; set; }
    public string AvailabilityStatus { get; set; } = FieldInventoryAvailabilityStatuses.Unknown;
    public string UtilizationStatus { get; set; } = FieldInventoryUtilizationStatuses.Unknown;
    public string? UsageType { get; set; }
    public string? UsedBy { get; set; }
    public string? AssignedGroup { get; set; }
    public string? AssignedDivision { get; set; }
    public string? AssignedTeamOrEvent { get; set; }
    public string SourceWorkbookUrl { get; set; } = "";
    public string SourceTab { get; set; } = "";
    public string SourceCellRange { get; set; } = "";
    public string SourceValue { get; set; } = "";
    public string? SourceColor { get; set; }
    public string ParserType { get; set; } = "";
    public string Confidence { get; set; } = FieldInventoryConfidence.Low;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class FieldInventoryCommitRunEntity
{
    public string Id { get; set; } = "";
    public string LeagueId { get; set; } = "";
    public string ImportRunId { get; set; } = "";
    public string SeasonLabel { get; set; } = "";
    public string Mode { get; set; } = FieldInventoryCommitModes.Import;
    public bool DryRun { get; set; }
    public int CreateCount { get; set; }
    public int UpdateCount { get; set; }
    public int DeleteCount { get; set; }
    public int UnchangedCount { get; set; }
    public int SkippedUnmappedCount { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class ParsedWorkbook
{
    public string SpreadsheetId { get; set; } = "";
    public string SourceWorkbookUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public List<ParsedWorkbookSheet> Sheets { get; set; } = new();
}

public class ParsedWorkbookSheet
{
    public string Name { get; set; } = "";
    public int Index { get; set; }
    public Dictionary<string, ParsedWorkbookCell> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ParsedWorkbookMergedRange> MergedRanges { get; set; } = new();
    public int MaxRow { get; set; }
    public int MaxColumn { get; set; }
    [JsonIgnore]
    public Dictionary<(int row, int column), ParsedWorkbookCell> CellsByIndex { get; set; } = new();
}

public class ParsedWorkbookCell
{
    public string Reference { get; set; } = "";
    public int Row { get; set; }
    public int Column { get; set; }
    public string Value { get; set; } = "";
    public string? BackgroundColor { get; set; }
}

public class ParsedWorkbookMergedRange
{
    public string Reference { get; set; } = "";
    public int StartRow { get; set; }
    public int EndRow { get; set; }
    public int StartColumn { get; set; }
    public int EndColumn { get; set; }
}

public record TabClassificationDecision(
    string ParserType,
    string ActionType,
    string Confidence,
    string Reason);

public class ParsedSheetResult
{
    public List<FieldInventoryStagedRecordEntity> Records { get; set; } = new();
    public List<FieldInventoryWarningEntity> Warnings { get; set; } = new();
    public List<FieldInventoryReviewQueueItemEntity> ReviewItems { get; set; } = new();
}
