using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Repositories;

public class FieldInventoryImportRepository : IFieldInventoryImportRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int WorkbookChunkSizeBytes = 48 * 1024;

    private readonly TableServiceClient _tableService;
    private readonly ILogger<FieldInventoryImportRepository> _logger;

    public FieldInventoryImportRepository(TableServiceClient tableService, ILogger<FieldInventoryImportRepository> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    public async Task UpsertImportRunAsync(FieldInventoryImportRunEntity run)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryImportRuns);
        var entity = new TableEntity(Constants.Pk.FieldInventoryImportRuns(run.LeagueId), run.Id)
        {
            ["LeagueId"] = run.LeagueId,
            ["SourceType"] = run.SourceType,
            ["SourceWorkbookUrl"] = run.SourceWorkbookUrl,
            ["UploadedWorkbookId"] = run.UploadedWorkbookId ?? "",
            ["SourceWorkbookName"] = run.SourceWorkbookName ?? "",
            ["SourceWorkbookTitle"] = run.SourceWorkbookTitle,
            ["SeasonLabel"] = run.SeasonLabel,
            ["SelectedTabsJson"] = JsonSerializer.Serialize(run.SelectedTabs, JsonOptions),
            ["Status"] = run.Status,
            ["CreatedAt"] = run.CreatedAt,
            ["UpdatedAt"] = run.UpdatedAt,
            ["CreatedBy"] = run.CreatedBy,
            ["SummaryCountsJson"] = JsonSerializer.Serialize(run.SummaryCounts, JsonOptions),
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    public async Task<FieldInventoryImportRunEntity?> GetImportRunAsync(string leagueId, string runId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryImportRuns);
        try
        {
            var entity = (await table.GetEntityAsync<TableEntity>(Constants.Pk.FieldInventoryImportRuns(leagueId), runId)).Value;
            return MapImportRun(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task ReplaceRunDataAsync(
        string importRunId,
        IEnumerable<FieldInventoryStagedRecordEntity> records,
        IEnumerable<FieldInventoryWarningEntity> warnings,
        IEnumerable<FieldInventoryReviewQueueItemEntity> reviewItems)
    {
        await ReplacePartitionAsync(
            Constants.Tables.FieldInventoryStagedRecords,
            Constants.Pk.FieldInventoryStagedRecords(importRunId),
            records.Select(MapStagedRecord));

        await ReplacePartitionAsync(
            Constants.Tables.FieldInventoryImportWarnings,
            Constants.Pk.FieldInventoryImportWarnings(importRunId),
            warnings.Select(MapWarning));

        await ReplacePartitionAsync(
            Constants.Tables.FieldInventoryReviewQueueItems,
            Constants.Pk.FieldInventoryReviewQueueItems(importRunId),
            reviewItems.Select(MapReviewItem));
    }

    public async Task<List<FieldInventoryStagedRecordEntity>> GetStagedRecordsAsync(string importRunId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryStagedRecords);
        var filter = ODataFilterBuilder.PartitionKeyExact(Constants.Pk.FieldInventoryStagedRecords(importRunId));
        var list = new List<FieldInventoryStagedRecordEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(MapStagedRecord(entity));
        }
        return list.OrderBy(x => x.Date).ThenBy(x => x.StartTime).ThenBy(x => x.FieldName ?? x.RawFieldName).ToList();
    }

    public async Task<List<FieldInventoryWarningEntity>> GetWarningsAsync(string importRunId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryImportWarnings);
        var filter = ODataFilterBuilder.PartitionKeyExact(Constants.Pk.FieldInventoryImportWarnings(importRunId));
        var list = new List<FieldInventoryWarningEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(MapWarning(entity));
        }
        return list;
    }

    public async Task<List<FieldInventoryReviewQueueItemEntity>> GetReviewItemsAsync(string importRunId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryReviewQueueItems);
        var filter = ODataFilterBuilder.PartitionKeyExact(Constants.Pk.FieldInventoryReviewQueueItems(importRunId));
        var list = new List<FieldInventoryReviewQueueItemEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(MapReviewItem(entity));
        }
        return list;
    }

    public async Task UpsertReviewItemAsync(FieldInventoryReviewQueueItemEntity reviewItem)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryReviewQueueItems);
        await table.UpsertEntityAsync(MapReviewItem(reviewItem), TableUpdateMode.Replace);
    }

    public async Task AddDiagnosticAsync(FieldInventoryDiagnosticEntity diagnostic)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryDiagnostics);
        var entity = new TableEntity(Constants.Pk.FieldInventoryDiagnostics(diagnostic.LeagueId, diagnostic.ClientRequestId), diagnostic.Id)
        {
            ["RunId"] = diagnostic.RunId ?? "",
            ["Stage"] = diagnostic.Stage,
            ["Status"] = diagnostic.Status,
            ["Message"] = diagnostic.Message,
            ["CreatedAt"] = diagnostic.CreatedAt,
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    public async Task<List<FieldInventoryDiagnosticEntity>> GetDiagnosticsAsync(string leagueId, string clientRequestId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryDiagnostics);
        var filter = ODataFilterBuilder.PartitionKeyExact(Constants.Pk.FieldInventoryDiagnostics(leagueId, clientRequestId));
        var list = new List<FieldInventoryDiagnosticEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(MapDiagnostic(entity));
        }

        return list.OrderBy(x => x.CreatedAt).ToList();
    }

    public async Task<List<FieldInventoryFieldAliasEntity>> GetFieldAliasesAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryFieldAliases);
        var filter = ODataFilterBuilder.PartitionKeyExact(Constants.Pk.FieldInventoryFieldAliases(leagueId));
        var list = new List<FieldInventoryFieldAliasEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(MapFieldAlias(entity));
        }
        return list.Where(x => x.IsActive).ToList();
    }

    public async Task UpsertFieldAliasAsync(FieldInventoryFieldAliasEntity alias)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryFieldAliases);
        await table.UpsertEntityAsync(MapFieldAlias(alias), TableUpdateMode.Replace);
    }

    public async Task<List<FieldInventoryTabClassificationEntity>> GetTabClassificationsAsync(string leagueId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryTabClassifications);
        var filter = ODataFilterBuilder.PartitionKeyExact(Constants.Pk.FieldInventoryTabClassifications(leagueId));
        var list = new List<FieldInventoryTabClassificationEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(MapTabClassification(entity));
        }
        return list.Where(x => x.IsActive).ToList();
    }

    public async Task UpsertTabClassificationAsync(FieldInventoryTabClassificationEntity classification)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryTabClassifications);
        await table.UpsertEntityAsync(MapTabClassification(classification), TableUpdateMode.Replace);
    }

    public async Task SaveWorkbookUploadAsync(FieldInventoryWorkbookUploadEntity upload, byte[] workbookBytes)
    {
        await ReplacePartitionAsync(
            Constants.Tables.FieldInventoryWorkbookUploads,
            Constants.Pk.FieldInventoryWorkbookUploads(upload.LeagueId, upload.Id),
            BuildWorkbookUploadEntities(upload, workbookBytes));
    }

    public async Task<FieldInventoryWorkbookUploadEntity?> GetWorkbookUploadAsync(string leagueId, string uploadId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryWorkbookUploads);
        try
        {
            var entity = (await table.GetEntityAsync<TableEntity>(Constants.Pk.FieldInventoryWorkbookUploads(leagueId, uploadId), "meta")).Value;
            return MapWorkbookUpload(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<byte[]?> GetWorkbookUploadBytesAsync(string leagueId, string uploadId)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryWorkbookUploads);
        var filter = ODataFilterBuilder.PartitionKeyExact(Constants.Pk.FieldInventoryWorkbookUploads(leagueId, uploadId));
        var chunks = new List<(string RowKey, byte[] Content)>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            if (!entity.RowKey.StartsWith("chunk|", StringComparison.OrdinalIgnoreCase)) continue;
            var content = entity.GetBinary("Content");
            if (content is null || content.Length == 0) continue;
            chunks.Add((entity.RowKey, content));
        }

        if (chunks.Count == 0) return null;

        using var stream = new MemoryStream();
        foreach (var chunk in chunks.OrderBy(x => x.RowKey, StringComparer.OrdinalIgnoreCase))
        {
            stream.Write(chunk.Content, 0, chunk.Content.Length);
        }

        return stream.ToArray();
    }

    public async Task<List<FieldInventoryLiveRecordEntity>> GetLiveRecordsAsync(string leagueId, string seasonLabel)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryLiveRecords);
        var filter = ODataFilterBuilder.PartitionKeyExact(Constants.Pk.FieldInventoryLiveRecords(leagueId, seasonLabel));
        var list = new List<FieldInventoryLiveRecordEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter))
        {
            list.Add(MapLiveRecord(entity));
        }
        return list;
    }

    public async Task ReplaceLiveRecordsAsync(string leagueId, string seasonLabel, IEnumerable<FieldInventoryLiveRecordEntity> records)
    {
        await ReplacePartitionAsync(
            Constants.Tables.FieldInventoryLiveRecords,
            Constants.Pk.FieldInventoryLiveRecords(leagueId, seasonLabel),
            records.Select(MapLiveRecord));
    }

    public async Task AddCommitRunAsync(FieldInventoryCommitRunEntity commitRun)
    {
        var table = await TableClients.GetTableAsync(_tableService, Constants.Tables.FieldInventoryCommitRuns);
        var entity = new TableEntity(Constants.Pk.FieldInventoryCommitRuns(commitRun.LeagueId), commitRun.Id)
        {
            ["ImportRunId"] = commitRun.ImportRunId,
            ["SeasonLabel"] = commitRun.SeasonLabel,
            ["Mode"] = commitRun.Mode,
            ["DryRun"] = commitRun.DryRun,
            ["CreateCount"] = commitRun.CreateCount,
            ["UpdateCount"] = commitRun.UpdateCount,
            ["DeleteCount"] = commitRun.DeleteCount,
            ["UnchangedCount"] = commitRun.UnchangedCount,
            ["SkippedUnmappedCount"] = commitRun.SkippedUnmappedCount,
            ["CreatedBy"] = commitRun.CreatedBy,
            ["CreatedAt"] = commitRun.CreatedAt,
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    private async Task ReplacePartitionAsync(string tableName, string partitionKey, IEnumerable<TableEntity> newEntities)
    {
        var table = await TableClients.GetTableAsync(_tableService, tableName);
        var existing = new List<TableEntity>();
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: ODataFilterBuilder.PartitionKeyExact(partitionKey)))
        {
            existing.Add(entity);
        }

        foreach (var entity in existing)
        {
            await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
        }

        foreach (var entity in newEntities)
        {
            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }
    }

    private static FieldInventoryImportRunEntity MapImportRun(TableEntity entity)
        => new()
        {
            Id = entity.RowKey,
            LeagueId = entity.GetString("LeagueId") ?? "",
            SourceType = entity.GetString("SourceType") ?? FieldInventorySourceTypes.GoogleSheet,
            SourceWorkbookUrl = entity.GetString("SourceWorkbookUrl") ?? "",
            UploadedWorkbookId = EmptyAsNull(entity.GetString("UploadedWorkbookId")),
            SourceWorkbookName = EmptyAsNull(entity.GetString("SourceWorkbookName")),
            SourceWorkbookTitle = entity.GetString("SourceWorkbookTitle") ?? "",
            SeasonLabel = entity.GetString("SeasonLabel") ?? "",
            SelectedTabs = Deserialize<List<FieldInventorySelectedTab>>(entity.GetString("SelectedTabsJson")) ?? new(),
            Status = entity.GetString("Status") ?? FieldInventoryImportStatuses.Draft,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.MinValue,
            CreatedBy = entity.GetString("CreatedBy") ?? "",
            SummaryCounts = Deserialize<FieldInventorySummaryCountsDto>(entity.GetString("SummaryCountsJson"))
                ?? new FieldInventorySummaryCountsDto(0, 0, 0, 0, 0, 0, 0),
        };

    private static TableEntity MapStagedRecord(FieldInventoryStagedRecordEntity record)
    {
        var entity = new TableEntity(Constants.Pk.FieldInventoryStagedRecords(record.ImportRunId), record.Id)
        {
            ["LeagueId"] = record.LeagueId,
            ["FieldId"] = record.FieldId ?? "",
            ["FieldName"] = record.FieldName ?? "",
            ["RawFieldName"] = record.RawFieldName,
            ["Date"] = record.Date,
            ["DayOfWeek"] = record.DayOfWeek,
            ["StartTime"] = record.StartTime,
            ["EndTime"] = record.EndTime,
            ["SlotDurationMinutes"] = record.SlotDurationMinutes,
            ["AvailabilityStatus"] = record.AvailabilityStatus,
            ["UtilizationStatus"] = record.UtilizationStatus,
            ["UsageType"] = record.UsageType ?? "",
            ["UsedBy"] = record.UsedBy ?? "",
            ["AssignedGroup"] = record.AssignedGroup ?? "",
            ["AssignedDivision"] = record.AssignedDivision ?? "",
            ["AssignedTeamOrEvent"] = record.AssignedTeamOrEvent ?? "",
            ["SourceWorkbookUrl"] = record.SourceWorkbookUrl,
            ["SourceTab"] = record.SourceTab,
            ["SourceCellRange"] = record.SourceCellRange,
            ["SourceValue"] = record.SourceValue,
            ["SourceColor"] = record.SourceColor ?? "",
            ["ParserType"] = record.ParserType,
            ["Confidence"] = record.Confidence,
            ["WarningFlagsJson"] = JsonSerializer.Serialize(record.WarningFlags, JsonOptions),
            ["ReviewStatus"] = record.ReviewStatus,
            ["CreatedAt"] = record.CreatedAt,
            ["UpdatedAt"] = record.UpdatedAt,
        };
        return entity;
    }

    private static FieldInventoryStagedRecordEntity MapStagedRecord(TableEntity entity)
        => new()
        {
            Id = entity.RowKey,
            ImportRunId = entity.PartitionKey[(Constants.Pk.FieldInventoryStagedRecords("").Length)..],
            LeagueId = entity.GetString("LeagueId") ?? "",
            FieldId = EmptyAsNull(entity.GetString("FieldId")),
            FieldName = EmptyAsNull(entity.GetString("FieldName")),
            RawFieldName = entity.GetString("RawFieldName") ?? "",
            Date = entity.GetString("Date") ?? "",
            DayOfWeek = entity.GetString("DayOfWeek") ?? "",
            StartTime = entity.GetString("StartTime") ?? "",
            EndTime = entity.GetString("EndTime") ?? "",
            SlotDurationMinutes = (int)(entity.GetInt32("SlotDurationMinutes") ?? 0),
            AvailabilityStatus = entity.GetString("AvailabilityStatus") ?? FieldInventoryAvailabilityStatuses.Unknown,
            UtilizationStatus = entity.GetString("UtilizationStatus") ?? FieldInventoryUtilizationStatuses.Unknown,
            UsageType = EmptyAsNull(entity.GetString("UsageType")),
            UsedBy = EmptyAsNull(entity.GetString("UsedBy")),
            AssignedGroup = EmptyAsNull(entity.GetString("AssignedGroup")),
            AssignedDivision = EmptyAsNull(entity.GetString("AssignedDivision")),
            AssignedTeamOrEvent = EmptyAsNull(entity.GetString("AssignedTeamOrEvent")),
            SourceWorkbookUrl = entity.GetString("SourceWorkbookUrl") ?? "",
            SourceTab = entity.GetString("SourceTab") ?? "",
            SourceCellRange = entity.GetString("SourceCellRange") ?? "",
            SourceValue = entity.GetString("SourceValue") ?? "",
            SourceColor = EmptyAsNull(entity.GetString("SourceColor")),
            ParserType = entity.GetString("ParserType") ?? "",
            Confidence = entity.GetString("Confidence") ?? FieldInventoryConfidence.Low,
            WarningFlags = Deserialize<List<string>>(entity.GetString("WarningFlagsJson")) ?? new(),
            ReviewStatus = entity.GetString("ReviewStatus") ?? FieldInventoryReviewStatuses.None,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.MinValue,
        };

    private static TableEntity MapWarning(FieldInventoryWarningEntity warning)
    {
        var entity = new TableEntity(Constants.Pk.FieldInventoryImportWarnings(warning.ImportRunId), warning.Id)
        {
            ["Severity"] = warning.Severity,
            ["Code"] = warning.Code,
            ["Message"] = warning.Message,
            ["SourceTab"] = warning.SourceTab,
            ["SourceCellRange"] = warning.SourceCellRange,
            ["RelatedRecordId"] = warning.RelatedRecordId ?? "",
            ["CreatedAt"] = warning.CreatedAt,
        };
        return entity;
    }

    private static FieldInventoryWarningEntity MapWarning(TableEntity entity)
        => new()
        {
            Id = entity.RowKey,
            ImportRunId = entity.PartitionKey[(Constants.Pk.FieldInventoryImportWarnings("").Length)..],
            Severity = entity.GetString("Severity") ?? FieldInventoryWarningSeverities.Warning,
            Code = entity.GetString("Code") ?? "",
            Message = entity.GetString("Message") ?? "",
            SourceTab = entity.GetString("SourceTab") ?? "",
            SourceCellRange = entity.GetString("SourceCellRange") ?? "",
            RelatedRecordId = EmptyAsNull(entity.GetString("RelatedRecordId")),
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
        };

    private static TableEntity MapReviewItem(FieldInventoryReviewQueueItemEntity item)
    {
        var entity = new TableEntity(Constants.Pk.FieldInventoryReviewQueueItems(item.ImportRunId), item.Id)
        {
            ["ItemType"] = item.ItemType,
            ["Severity"] = item.Severity,
            ["Title"] = item.Title,
            ["Description"] = item.Description,
            ["SourceTab"] = item.SourceTab,
            ["SourceCellRange"] = item.SourceCellRange,
            ["RawValue"] = item.RawValue,
            ["SuggestedResolutionJson"] = JsonSerializer.Serialize(item.SuggestedResolution, JsonOptions),
            ["ChosenResolutionJson"] = JsonSerializer.Serialize(item.ChosenResolution, JsonOptions),
            ["Status"] = item.Status,
            ["SaveDecisionForFuture"] = item.SaveDecisionForFuture,
            ["CreatedAt"] = item.CreatedAt,
            ["UpdatedAt"] = item.UpdatedAt,
        };
        return entity;
    }

    private static FieldInventoryReviewQueueItemEntity MapReviewItem(TableEntity entity)
        => new()
        {
            Id = entity.RowKey,
            ImportRunId = entity.PartitionKey[(Constants.Pk.FieldInventoryReviewQueueItems("").Length)..],
            ItemType = entity.GetString("ItemType") ?? FieldInventoryReviewItemTypes.Other,
            Severity = entity.GetString("Severity") ?? FieldInventoryReviewItemSeverities.NonBlocking,
            Title = entity.GetString("Title") ?? "",
            Description = entity.GetString("Description") ?? "",
            SourceTab = entity.GetString("SourceTab") ?? "",
            SourceCellRange = entity.GetString("SourceCellRange") ?? "",
            RawValue = entity.GetString("RawValue") ?? "",
            SuggestedResolution = Deserialize<Dictionary<string, string?>>(entity.GetString("SuggestedResolutionJson")) ?? new(),
            ChosenResolution = Deserialize<Dictionary<string, string?>>(entity.GetString("ChosenResolutionJson")) ?? new(),
            Status = entity.GetString("Status") ?? FieldInventoryReviewItemStatuses.Open,
            SaveDecisionForFuture = entity.GetBoolean("SaveDecisionForFuture") ?? false,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.MinValue,
        };

    private static TableEntity MapFieldAlias(FieldInventoryFieldAliasEntity alias)
    {
        var entity = new TableEntity(Constants.Pk.FieldInventoryFieldAliases(alias.LeagueId), alias.NormalizedLookupKey)
        {
            ["Id"] = alias.Id,
            ["RawFieldName"] = alias.RawFieldName,
            ["NormalizedLookupKey"] = alias.NormalizedLookupKey,
            ["CanonicalFieldId"] = alias.CanonicalFieldId,
            ["CanonicalFieldName"] = alias.CanonicalFieldName,
            ["IsActive"] = alias.IsActive,
            ["CreatedAt"] = alias.CreatedAt,
            ["UpdatedAt"] = alias.UpdatedAt,
            ["CreatedBy"] = alias.CreatedBy,
        };
        return entity;
    }

    private static FieldInventoryFieldAliasEntity MapFieldAlias(TableEntity entity)
        => new()
        {
            Id = entity.GetString("Id") ?? entity.RowKey,
            LeagueId = entity.PartitionKey[(Constants.Pk.FieldInventoryFieldAliases("").Length)..],
            RawFieldName = entity.GetString("RawFieldName") ?? "",
            NormalizedLookupKey = entity.GetString("NormalizedLookupKey") ?? entity.RowKey,
            CanonicalFieldId = entity.GetString("CanonicalFieldId") ?? "",
            CanonicalFieldName = entity.GetString("CanonicalFieldName") ?? "",
            IsActive = entity.GetBoolean("IsActive") ?? true,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.MinValue,
            CreatedBy = entity.GetString("CreatedBy") ?? "",
        };

    private static TableEntity MapTabClassification(FieldInventoryTabClassificationEntity classification)
    {
        var rowKey = Slug.Make(classification.RawTabName);
        var entity = new TableEntity(Constants.Pk.FieldInventoryTabClassifications(classification.LeagueId), rowKey)
        {
            ["Id"] = classification.Id,
            ["WorkbookTitlePattern"] = classification.WorkbookTitlePattern,
            ["RawTabName"] = classification.RawTabName,
            ["ParserType"] = classification.ParserType,
            ["ActionType"] = classification.ActionType,
            ["IsActive"] = classification.IsActive,
            ["CreatedAt"] = classification.CreatedAt,
            ["UpdatedAt"] = classification.UpdatedAt,
        };
        return entity;
    }

    private static FieldInventoryTabClassificationEntity MapTabClassification(TableEntity entity)
        => new()
        {
            Id = entity.GetString("Id") ?? entity.RowKey,
            LeagueId = entity.PartitionKey[(Constants.Pk.FieldInventoryTabClassifications("").Length)..],
            WorkbookTitlePattern = entity.GetString("WorkbookTitlePattern") ?? "",
            RawTabName = entity.GetString("RawTabName") ?? entity.RowKey,
            ParserType = entity.GetString("ParserType") ?? "",
            ActionType = entity.GetString("ActionType") ?? "",
            IsActive = entity.GetBoolean("IsActive") ?? true,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.MinValue,
        };

    private static FieldInventoryDiagnosticEntity MapDiagnostic(TableEntity entity)
        => new()
        {
            Id = entity.RowKey,
            LeagueId = entity.PartitionKey.Split('|', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? "",
            ClientRequestId = entity.PartitionKey.Split('|', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "",
            RunId = EmptyAsNull(entity.GetString("RunId")),
            Stage = entity.GetString("Stage") ?? "",
            Status = entity.GetString("Status") ?? "",
            Message = entity.GetString("Message") ?? "",
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
        };

    private static TableEntity MapLiveRecord(FieldInventoryLiveRecordEntity record)
    {
        var entity = new TableEntity(Constants.Pk.FieldInventoryLiveRecords(record.LeagueId, record.SeasonLabel), record.Id)
        {
            ["ImportRunId"] = record.ImportRunId,
            ["FieldId"] = record.FieldId,
            ["FieldName"] = record.FieldName,
            ["RawFieldName"] = record.RawFieldName,
            ["Date"] = record.Date,
            ["DayOfWeek"] = record.DayOfWeek,
            ["StartTime"] = record.StartTime,
            ["EndTime"] = record.EndTime,
            ["SlotDurationMinutes"] = record.SlotDurationMinutes,
            ["AvailabilityStatus"] = record.AvailabilityStatus,
            ["UtilizationStatus"] = record.UtilizationStatus,
            ["UsageType"] = record.UsageType ?? "",
            ["UsedBy"] = record.UsedBy ?? "",
            ["AssignedGroup"] = record.AssignedGroup ?? "",
            ["AssignedDivision"] = record.AssignedDivision ?? "",
            ["AssignedTeamOrEvent"] = record.AssignedTeamOrEvent ?? "",
            ["SourceWorkbookUrl"] = record.SourceWorkbookUrl,
            ["SourceTab"] = record.SourceTab,
            ["SourceCellRange"] = record.SourceCellRange,
            ["SourceValue"] = record.SourceValue,
            ["SourceColor"] = record.SourceColor ?? "",
            ["ParserType"] = record.ParserType,
            ["Confidence"] = record.Confidence,
            ["CreatedAt"] = record.CreatedAt,
            ["UpdatedAt"] = record.UpdatedAt,
        };
        return entity;
    }

    private static FieldInventoryLiveRecordEntity MapLiveRecord(TableEntity entity)
        => new()
        {
            Id = entity.RowKey,
            LeagueId = ExtractLeagueIdFromLivePartition(entity.PartitionKey),
            SeasonLabel = ExtractSeasonFromLivePartition(entity.PartitionKey),
            ImportRunId = entity.GetString("ImportRunId") ?? "",
            FieldId = entity.GetString("FieldId") ?? "",
            FieldName = entity.GetString("FieldName") ?? "",
            RawFieldName = entity.GetString("RawFieldName") ?? "",
            Date = entity.GetString("Date") ?? "",
            DayOfWeek = entity.GetString("DayOfWeek") ?? "",
            StartTime = entity.GetString("StartTime") ?? "",
            EndTime = entity.GetString("EndTime") ?? "",
            SlotDurationMinutes = entity.GetInt32("SlotDurationMinutes") ?? 0,
            AvailabilityStatus = entity.GetString("AvailabilityStatus") ?? FieldInventoryAvailabilityStatuses.Unknown,
            UtilizationStatus = entity.GetString("UtilizationStatus") ?? FieldInventoryUtilizationStatuses.Unknown,
            UsageType = EmptyAsNull(entity.GetString("UsageType")),
            UsedBy = EmptyAsNull(entity.GetString("UsedBy")),
            AssignedGroup = EmptyAsNull(entity.GetString("AssignedGroup")),
            AssignedDivision = EmptyAsNull(entity.GetString("AssignedDivision")),
            AssignedTeamOrEvent = EmptyAsNull(entity.GetString("AssignedTeamOrEvent")),
            SourceWorkbookUrl = entity.GetString("SourceWorkbookUrl") ?? "",
            SourceTab = entity.GetString("SourceTab") ?? "",
            SourceCellRange = entity.GetString("SourceCellRange") ?? "",
            SourceValue = entity.GetString("SourceValue") ?? "",
            SourceColor = EmptyAsNull(entity.GetString("SourceColor")),
            ParserType = entity.GetString("ParserType") ?? "",
            Confidence = entity.GetString("Confidence") ?? FieldInventoryConfidence.Low,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.MinValue,
        };

    private static string ExtractLeagueIdFromLivePartition(string partitionKey)
    {
        var parts = partitionKey.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[1] : "";
    }

    private static string ExtractSeasonFromLivePartition(string partitionKey)
    {
        var parts = partitionKey.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : "";
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static string? EmptyAsNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IEnumerable<TableEntity> BuildWorkbookUploadEntities(FieldInventoryWorkbookUploadEntity upload, byte[] workbookBytes)
    {
        var partitionKey = Constants.Pk.FieldInventoryWorkbookUploads(upload.LeagueId, upload.Id);
        yield return new TableEntity(partitionKey, "meta")
        {
            ["LeagueId"] = upload.LeagueId,
            ["FileName"] = upload.FileName,
            ["ContentType"] = upload.ContentType,
            ["ByteCount"] = upload.ByteCount,
            ["CreatedAt"] = upload.CreatedAt,
            ["UpdatedAt"] = upload.UpdatedAt,
            ["CreatedBy"] = upload.CreatedBy,
        };

        var chunkIndex = 0;
        for (var offset = 0; offset < workbookBytes.Length; offset += WorkbookChunkSizeBytes)
        {
            var count = Math.Min(WorkbookChunkSizeBytes, workbookBytes.Length - offset);
            var chunk = new byte[count];
            Buffer.BlockCopy(workbookBytes, offset, chunk, 0, count);
            yield return new TableEntity(partitionKey, $"chunk|{chunkIndex:D6}")
            {
                ["Content"] = chunk,
                ["ByteCount"] = count,
            };
            chunkIndex++;
        }
    }

    private static FieldInventoryWorkbookUploadEntity MapWorkbookUpload(TableEntity entity)
        => new()
        {
            Id = entity.PartitionKey.Split('|', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "",
            LeagueId = entity.GetString("LeagueId") ?? "",
            FileName = entity.GetString("FileName") ?? "",
            ContentType = entity.GetString("ContentType") ?? "",
            ByteCount = entity.GetInt64("ByteCount") ?? 0,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.MinValue,
            CreatedBy = entity.GetString("CreatedBy") ?? "",
        };
}
