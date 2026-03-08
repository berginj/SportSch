using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Data.Tables;
using GameSwap.Functions.Models;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameSwap.Tests.Services;

public class FieldInventoryImportServiceTests
{
    private readonly InMemoryFieldInventoryImportRepository _repository = new();
    private readonly InMemoryFieldRepository _fieldRepository = new();
    private readonly Mock<ILogger<FieldInventoryImportService>> _logger = new();

    [Fact]
    public async Task InspectWorkbookAsync_InvalidUrl_ThrowsBadRequest()
    {
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbook()));
        var context = CorrelationContext.Create("user-1", "league-1");

        var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
            service.InspectWorkbookAsync("https://example.com/not-google", context));

        Assert.Equal(400, ex.Status);
        Assert.Equal(ErrorCodes.INVALID_WORKBOOK_URL, ex.Code);
    }

    [Fact]
    public void NormalizeWorkbookUrl_PreservesResourceKey()
    {
        var normalized = FieldInventoryImportService.GoogleSheetUrlParser.NormalizeWorkbookUrl(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit?gid=0&resourcekey=abc-123");

        Assert.Equal("https://docs.google.com/spreadsheets/d/test-sheet/edit?resourcekey=abc-123", normalized);
        Assert.Equal("abc-123", FieldInventoryImportService.GoogleSheetUrlParser.ExtractResourceKey(normalized));
    }

    [Fact]
    public void BuildWorkbookExportUrls_IncludesFallbackVariants()
    {
        var urls = FieldInventoryImportService.GoogleSheetUrlParser.BuildWorkbookExportUrls(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit?resourcekey=abc-123");

        Assert.Equal(4, urls.Count);
        Assert.Equal("https://docs.google.com/spreadsheets/d/test-sheet/export?format=xlsx&id=test-sheet&resourcekey=abc-123", urls[0]);
        Assert.Equal("https://docs.google.com/spreadsheets/d/test-sheet/export?exportFormat=xlsx&format=xlsx&id=test-sheet&resourcekey=abc-123", urls[1]);
        Assert.Equal("https://docs.google.com/spreadsheets/d/test-sheet/export?format=xlsx&resourcekey=abc-123", urls[2]);
        Assert.Equal("https://docs.google.com/spreadsheets/d/test-sheet/pub?output=xlsx", urls[3]);
    }

    [Fact]
    public async Task InspectWorkbookAsync_ClassifiesKnownTabs()
    {
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbook()));
        var result = await service.InspectWorkbookAsync("https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0", CorrelationContext.Create("user-1", "league-1"));

        Assert.Equal("Spring 2026 County Inventory", result.SourceWorkbookTitle);
        Assert.Contains(result.Tabs, x => x.TabName == "Spring 3/16-5/22" && x.InferredParserType == FieldInventoryParserTypes.SeasonWeekdayGrid);
        Assert.Contains(result.Tabs, x => x.TabName == "Weekends" && x.InferredParserType == FieldInventoryParserTypes.WeekendGrid);
        Assert.Contains(result.Tabs, x => x.TabName == "County Grid" && x.InferredActionType == FieldInventoryActionTypes.Reference);
        Assert.Contains(result.Tabs, x => x.TabName == "Request Forms" && x.InferredActionType == FieldInventoryActionTypes.Ignore);
    }

    [Fact]
    public async Task CreatePreviewAsync_ParsesWeekdayAndWeekendTabsAndRespectsBlankCells()
    {
        _fieldRepository.AddField("league-1", "park-a", "field-1", "Park A", "Field 1", "Park A > Field 1");
        _fieldRepository.AddField("league-1", "park-a", "field-2", "Park A", "Field 2", "Park A > Field 2");
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbook()));

        var preview = await service.CreatePreviewAsync(new FieldInventoryPreviewRequest(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            "Spring 2026",
            new List<FieldInventorySelectedTab>
            {
                new("Spring 3/16-5/22", FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, true),
                new("Weekends", FieldInventoryParserTypes.WeekendGrid, FieldInventoryActionTypes.Ingest, true),
            }), CorrelationContext.Create("user-1", "league-1"));

        Assert.Equal(5, preview.Run.SummaryCounts.ParsedRecords);
        Assert.DoesNotContain(preview.Records, x => x.SourceCellRange == "C3");
        Assert.Contains(preview.Records, x => x.SourceCellRange == "B3:C3" && x.StartTime == "18:00" && x.EndTime == "20:00");
        Assert.Contains(preview.Records, x => x.AvailabilityStatus == FieldInventoryAvailabilityStatuses.Available && x.UtilizationStatus == FieldInventoryUtilizationStatuses.NotUsed);
        Assert.Contains(preview.Records, x => x.UtilizationStatus == FieldInventoryUtilizationStatuses.Used && x.AssignedDivision == "10U");
    }

    [Fact]
    public async Task CreatePreviewAsync_ReferenceTabCreatesWarningsOnly()
    {
        _fieldRepository.AddField("league-1", "park-a", "field-1", "Park A", "Field 1", "Park A > Field 1");
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbook()));

        var preview = await service.CreatePreviewAsync(new FieldInventoryPreviewRequest(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            "Spring 2026",
            new List<FieldInventorySelectedTab>
            {
                new("County Grid", FieldInventoryParserTypes.ReferenceGrid, FieldInventoryActionTypes.Reference, true),
            }), CorrelationContext.Create("user-1", "league-1"));

        Assert.Empty(preview.Records);
        Assert.Single(preview.Warnings);
        Assert.Equal("reference_tab", preview.Warnings[0].Code);
    }

    [Fact]
    public async Task CreatePreviewAsync_UnmappedFieldCreatesReviewQueueItem()
    {
        _fieldRepository.AddField("league-1", "park-a", "field-1", "Park A", "Field 1", "Park A > Field 1");
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbookWithUnmappedField()));

        var preview = await service.CreatePreviewAsync(new FieldInventoryPreviewRequest(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            "Spring 2026",
            new List<FieldInventorySelectedTab>
            {
                new("Spring 3/16-5/22", FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, true),
            }), CorrelationContext.Create("user-1", "league-1"));

        Assert.Equal(1, preview.Run.SummaryCounts.UnmappedFields);
        Assert.Contains(preview.ReviewItems, x => x.ItemType == FieldInventoryReviewItemTypes.FieldMapping);
    }

    [Fact]
    public async Task SaveFieldAliasAsync_RerunsPreviewWithMappedField()
    {
        _fieldRepository.AddField("league-1", "park-a", "field-1", "Park A", "Field 1", "Park A > Field 1");
        _fieldRepository.AddField("league-1", "park-b", "field-9", "Park B", "Diamond 9", "Park B > Diamond 9");
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbookWithUnmappedField()));

        var preview = await service.CreatePreviewAsync(new FieldInventoryPreviewRequest(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            "Spring 2026",
            new List<FieldInventorySelectedTab>
            {
                new("Spring 3/16-5/22", FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, true),
            }), CorrelationContext.Create("user-1", "league-1"));

        var rerun = await service.SaveFieldAliasAsync(new FieldInventoryAliasSaveRequest(
            "County Diamond 9",
            "park-b/field-9",
            "Park B > Diamond 9",
            preview.Run.Id,
            true), CorrelationContext.Create("user-1", "league-1"));

        Assert.Equal(0, rerun.Run.SummaryCounts.UnmappedFields);
        Assert.DoesNotContain(rerun.ReviewItems, x => x.ItemType == FieldInventoryReviewItemTypes.FieldMapping);
        Assert.All(rerun.Records, x => Assert.False(string.IsNullOrWhiteSpace(x.FieldId)));
    }

    [Fact]
    public async Task StageRunAsync_UpdatesRunStatus()
    {
        _fieldRepository.AddField("league-1", "park-a", "field-1", "Park A", "Field 1", "Park A > Field 1");
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbook()));

        var preview = await service.CreatePreviewAsync(new FieldInventoryPreviewRequest(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            "Spring 2026",
            new List<FieldInventorySelectedTab>
            {
                new("Spring 3/16-5/22", FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, true),
            }), CorrelationContext.Create("user-1", "league-1"));

        var staged = await service.StageRunAsync(preview.Run.Id, CorrelationContext.Create("user-1", "league-1"));
        Assert.Equal(FieldInventoryImportStatuses.Staged, staged.Run.Status);
    }

    [Fact]
    public async Task CommitRunAsync_DryRunDoesNotWriteLiveRecords()
    {
        _fieldRepository.AddField("league-1", "park-a", "field-1", "Park A", "Field 1", "Park A > Field 1");
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbook()));
        var preview = await service.CreatePreviewAsync(new FieldInventoryPreviewRequest(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            "Spring 2026",
            new List<FieldInventorySelectedTab>
            {
                new("Spring 3/16-5/22", FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, true),
            }), CorrelationContext.Create("user-1", "league-1"));

        var result = await service.CommitRunAsync(preview.Run.Id, new FieldInventoryCommitRequest(FieldInventoryCommitModes.Upsert, true, true), CorrelationContext.Create("user-1", "league-1"));

        Assert.NotNull(result.CommitPreview);
        Assert.Empty(await _repository.GetLiveRecordsAsync("league-1", "Spring 2026"));
    }

    [Fact]
    public async Task CommitRunAsync_UpsertWritesLiveRecords()
    {
        _fieldRepository.AddField("league-1", "park-a", "field-1", "Park A", "Field 1", "Park A > Field 1");
        var service = CreateService(new StaticWorkbookConnector(BuildWorkbook()));
        var preview = await service.CreatePreviewAsync(new FieldInventoryPreviewRequest(
            "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            "Spring 2026",
            new List<FieldInventorySelectedTab>
            {
                new("Spring 3/16-5/22", FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, true),
            }), CorrelationContext.Create("user-1", "league-1"));

        var result = await service.CommitRunAsync(preview.Run.Id, new FieldInventoryCommitRequest(FieldInventoryCommitModes.Upsert, false, true), CorrelationContext.Create("user-1", "league-1"));
        var live = await _repository.GetLiveRecordsAsync("league-1", "Spring 2026");

        Assert.Equal(FieldInventoryImportStatuses.Imported, result.Run.Status);
        Assert.NotEmpty(live);
    }

    private FieldInventoryImportService CreateService(FieldInventoryImportService.IFieldInventoryWorkbookConnector connector)
        => new(_repository, _fieldRepository, connector, _logger.Object);

    private static ParsedWorkbook BuildWorkbook()
        => new()
        {
            SpreadsheetId = "test-sheet",
            SourceWorkbookUrl = "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            Title = "Spring 2026 County Inventory",
            Sheets =
            {
                BuildWeekdaySheet("Spring 3/16-5/22", "Park A > Field 1", "Park A > Field 2"),
                BuildWeekendSheet("Weekends", "Park A > Field 1"),
                BuildReferenceSheet(),
                new ParsedWorkbookSheet { Name = "Request Forms", Index = 3 },
            }
        };

    private static ParsedWorkbook BuildWorkbookWithUnmappedField()
        => new()
        {
            SpreadsheetId = "test-sheet",
            SourceWorkbookUrl = "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0",
            Title = "Spring 2026 County Inventory",
            Sheets =
            {
                BuildWeekdaySheet("Spring 3/16-5/22", "County Diamond 9", "Park A > Field 1"),
            }
        };

    private static ParsedWorkbookSheet BuildWeekdaySheet(string name, string firstField, string secondField)
    {
        var sheet = new ParsedWorkbookSheet { Name = name, Index = 0, MaxRow = 4, MaxColumn = 4 };
        AddCell(sheet, "A1", "Date");
        AddCell(sheet, "B1", firstField);
        AddCell(sheet, "D1", secondField);
        AddCell(sheet, "B2", "18:00");
        AddCell(sheet, "C2", "19:00");
        AddCell(sheet, "D2", "20:00");
        AddCell(sheet, "A3", "3/16/2026");
        AddCell(sheet, "B3", "Practice 10U");
        AddCell(sheet, "D3", "Available");
        AddCell(sheet, "A4", "3/17/2026");
        AddCell(sheet, "B4", "County Use");
        AddCell(sheet, "D4", "Pending");
        sheet.MergedRanges.Add(new ParsedWorkbookMergedRange
        {
            Reference = "B3:C3",
            StartRow = 3,
            EndRow = 3,
            StartColumn = 2,
            EndColumn = 3
        });
        return sheet;
    }

    private static ParsedWorkbookSheet BuildWeekendSheet(string name, string fieldName)
    {
        var sheet = new ParsedWorkbookSheet { Name = name, Index = 1, MaxRow = 3, MaxColumn = 3 };
        AddCell(sheet, "A1", "Date");
        AddCell(sheet, "B1", fieldName);
        AddCell(sheet, "B2", "09:00");
        AddCell(sheet, "C2", "10:00");
        AddCell(sheet, "A3", "3/21/2026");
        AddCell(sheet, "B3", "Available");
        return sheet;
    }

    private static ParsedWorkbookSheet BuildReferenceSheet()
    {
        var sheet = new ParsedWorkbookSheet { Name = "County Grid", Index = 2, MaxRow = 2, MaxColumn = 2 };
        AddCell(sheet, "A1", "Date");
        AddCell(sheet, "B1", "County Grid");
        AddCell(sheet, "A2", "3/18/2026");
        AddCell(sheet, "B2", "Outside Use");
        return sheet;
    }

    private static void AddCell(ParsedWorkbookSheet sheet, string reference, string value)
    {
        ParseReference(reference, out var row, out var column);
        var cell = new ParsedWorkbookCell
        {
            Reference = reference,
            Row = row,
            Column = column,
            Value = value
        };
        sheet.Cells[reference] = cell;
        sheet.CellsByIndex[(row, column)] = cell;
        sheet.MaxRow = Math.Max(sheet.MaxRow, row);
        sheet.MaxColumn = Math.Max(sheet.MaxColumn, column);
    }

    private static void ParseReference(string reference, out int row, out int column)
    {
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var numbers = new string(reference.SkipWhile(char.IsLetter).ToArray());
        row = int.Parse(numbers);
        column = 0;
        foreach (var ch in letters)
        {
            column = (column * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
    }

    private sealed class StaticWorkbookConnector : FieldInventoryImportService.IFieldInventoryWorkbookConnector
    {
        private readonly ParsedWorkbook _workbook;

        public StaticWorkbookConnector(ParsedWorkbook workbook)
        {
            _workbook = workbook;
        }

        public Task<ParsedWorkbook> LoadWorkbookAsync(string sourceWorkbookUrl)
        {
            return Task.FromResult(_workbook);
        }
    }

    private sealed class InMemoryFieldInventoryImportRepository : IFieldInventoryImportRepository
    {
        private readonly Dictionary<string, FieldInventoryImportRunEntity> _runs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryStagedRecordEntity>> _records = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryWarningEntity>> _warnings = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryReviewQueueItemEntity>> _reviewItems = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryFieldAliasEntity>> _aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryTabClassificationEntity>> _tabClassifications = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryLiveRecordEntity>> _liveRecords = new(StringComparer.OrdinalIgnoreCase);

        public Task UpsertImportRunAsync(FieldInventoryImportRunEntity run)
        {
            _runs[$"{run.LeagueId}|{run.Id}"] = run;
            return Task.CompletedTask;
        }

        public Task<FieldInventoryImportRunEntity?> GetImportRunAsync(string leagueId, string runId)
        {
            _runs.TryGetValue($"{leagueId}|{runId}", out var run);
            return Task.FromResult(run);
        }

        public Task ReplaceRunDataAsync(string importRunId, IEnumerable<FieldInventoryStagedRecordEntity> records, IEnumerable<FieldInventoryWarningEntity> warnings, IEnumerable<FieldInventoryReviewQueueItemEntity> reviewItems)
        {
            _records[importRunId] = records.ToList();
            _warnings[importRunId] = warnings.ToList();
            _reviewItems[importRunId] = reviewItems.ToList();
            return Task.CompletedTask;
        }

        public Task<List<FieldInventoryStagedRecordEntity>> GetStagedRecordsAsync(string importRunId)
            => Task.FromResult(_records.TryGetValue(importRunId, out var list) ? list.ToList() : new List<FieldInventoryStagedRecordEntity>());

        public Task<List<FieldInventoryWarningEntity>> GetWarningsAsync(string importRunId)
            => Task.FromResult(_warnings.TryGetValue(importRunId, out var list) ? list.ToList() : new List<FieldInventoryWarningEntity>());

        public Task<List<FieldInventoryReviewQueueItemEntity>> GetReviewItemsAsync(string importRunId)
            => Task.FromResult(_reviewItems.TryGetValue(importRunId, out var list) ? list.ToList() : new List<FieldInventoryReviewQueueItemEntity>());

        public Task UpsertReviewItemAsync(FieldInventoryReviewQueueItemEntity reviewItem)
        {
            if (!_reviewItems.ContainsKey(reviewItem.ImportRunId))
            {
                _reviewItems[reviewItem.ImportRunId] = new List<FieldInventoryReviewQueueItemEntity>();
            }

            var list = _reviewItems[reviewItem.ImportRunId];
            var index = list.FindIndex(x => x.Id == reviewItem.Id);
            if (index >= 0) list[index] = reviewItem;
            else list.Add(reviewItem);
            return Task.CompletedTask;
        }

        public Task<List<FieldInventoryFieldAliasEntity>> GetFieldAliasesAsync(string leagueId)
            => Task.FromResult(_aliases.TryGetValue(leagueId, out var list) ? list.ToList() : new List<FieldInventoryFieldAliasEntity>());

        public Task UpsertFieldAliasAsync(FieldInventoryFieldAliasEntity alias)
        {
            if (!_aliases.ContainsKey(alias.LeagueId))
            {
                _aliases[alias.LeagueId] = new List<FieldInventoryFieldAliasEntity>();
            }

            _aliases[alias.LeagueId].RemoveAll(x => x.NormalizedLookupKey == alias.NormalizedLookupKey);
            _aliases[alias.LeagueId].Add(alias);
            return Task.CompletedTask;
        }

        public Task<List<FieldInventoryTabClassificationEntity>> GetTabClassificationsAsync(string leagueId)
            => Task.FromResult(_tabClassifications.TryGetValue(leagueId, out var list) ? list.ToList() : new List<FieldInventoryTabClassificationEntity>());

        public Task UpsertTabClassificationAsync(FieldInventoryTabClassificationEntity classification)
        {
            if (!_tabClassifications.ContainsKey(classification.LeagueId))
            {
                _tabClassifications[classification.LeagueId] = new List<FieldInventoryTabClassificationEntity>();
            }

            _tabClassifications[classification.LeagueId].RemoveAll(x => x.RawTabName == classification.RawTabName);
            _tabClassifications[classification.LeagueId].Add(classification);
            return Task.CompletedTask;
        }

        public Task<List<FieldInventoryLiveRecordEntity>> GetLiveRecordsAsync(string leagueId, string seasonLabel)
            => Task.FromResult(_liveRecords.TryGetValue($"{leagueId}|{seasonLabel}", out var list) ? list.ToList() : new List<FieldInventoryLiveRecordEntity>());

        public Task ReplaceLiveRecordsAsync(string leagueId, string seasonLabel, IEnumerable<FieldInventoryLiveRecordEntity> records)
        {
            _liveRecords[$"{leagueId}|{seasonLabel}"] = records.ToList();
            return Task.CompletedTask;
        }

        public Task AddCommitRunAsync(FieldInventoryCommitRunEntity commitRun)
            => Task.CompletedTask;
    }

    private sealed class InMemoryFieldRepository : IFieldRepository
    {
        private readonly List<TableEntity> _fields = new();

        public void AddField(string leagueId, string parkCode, string fieldCode, string parkName, string fieldName, string displayName)
        {
            _fields.Add(new TableEntity($"FIELD|{leagueId}|{parkCode}", fieldCode)
            {
                ["ParkName"] = parkName,
                ["FieldName"] = fieldName,
                ["DisplayName"] = displayName,
                ["IsActive"] = true
            });
        }

        public Task<TableEntity?> GetFieldAsync(string leagueId, string parkCode, string fieldCode)
            => Task.FromResult(_fields.FirstOrDefault(x => x.PartitionKey == $"FIELD|{leagueId}|{parkCode}" && x.RowKey == fieldCode));

        public Task<TableEntity?> GetFieldByKeyAsync(string leagueId, string fieldKey)
        {
            var parts = fieldKey.Split('/');
            return Task.FromResult(parts.Length == 2 ? _fields.FirstOrDefault(x => x.PartitionKey == $"FIELD|{leagueId}|{parts[0]}" && x.RowKey == parts[1]) : null);
        }

        public Task<List<TableEntity>> QueryFieldsAsync(string leagueId, string? parkCode = null)
            => Task.FromResult(_fields.Where(x => x.PartitionKey.StartsWith($"FIELD|{leagueId}|", StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<bool> FieldExistsAsync(string leagueId, string parkCode, string fieldCode)
            => Task.FromResult(_fields.Any(x => x.PartitionKey == $"FIELD|{leagueId}|{parkCode}" && x.RowKey == fieldCode));

        public Task CreateFieldAsync(TableEntity field) => Task.CompletedTask;
        public Task UpdateFieldAsync(TableEntity field) => Task.CompletedTask;
        public Task DeleteFieldAsync(string leagueId, string parkCode, string fieldCode) => Task.CompletedTask;
        public Task DeactivateFieldAsync(string leagueId, string parkCode, string fieldCode) => Task.CompletedTask;
    }
}
