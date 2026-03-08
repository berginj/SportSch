using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GameSwap.Functions.Models;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Services;

public class FieldInventoryImportService : IFieldInventoryImportService
{
    private readonly IFieldInventoryImportRepository _repository;
    private readonly IFieldRepository _fieldRepository;
    private readonly ILogger<FieldInventoryImportService> _logger;
    private readonly IFieldInventoryWorkbookConnector _workbookConnector;

    public FieldInventoryImportService(
        IFieldInventoryImportRepository repository,
        IFieldRepository fieldRepository,
        IFieldInventoryWorkbookConnector workbookConnector,
        ILogger<FieldInventoryImportService> logger)
    {
        _repository = repository;
        _fieldRepository = fieldRepository;
        _workbookConnector = workbookConnector;
        _logger = logger;
    }

    public FieldInventoryImportService(
        IFieldInventoryImportRepository repository,
        IFieldRepository fieldRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<FieldInventoryImportService> logger)
        : this(repository, fieldRepository, (IFieldInventoryWorkbookConnector)new PublicGoogleSheetsWorkbookConnector(httpClientFactory), logger)
    {
    }

    public async Task<FieldInventoryWorkbookInspectResponse> InspectWorkbookAsync(string sourceWorkbookUrl, CorrelationContext context)
    {
        var normalizedUrl = GoogleSheetUrlParser.NormalizeWorkbookUrl(sourceWorkbookUrl);
        var workbook = await LoadWorkbookAsync(normalizedUrl);
        var classifications = await _repository.GetTabClassificationsAsync(context.LeagueId);

        return new FieldInventoryWorkbookInspectResponse(
            normalizedUrl,
            workbook.SpreadsheetId,
            workbook.Title,
            workbook.Sheets
                .OrderBy(x => x.Index)
                .Select(sheet =>
                {
                    var decision = InferTabClassification(sheet, workbook.Title, classifications);
                    return new FieldInventoryWorkbookTabDto(
                        sheet.Name,
                        sheet.Index,
                        sheet.IsHidden,
                        decision.ParserType,
                        decision.ActionType,
                        decision.Confidence,
                        decision.Reason,
                        sheet.Cells.Count(x => !string.IsNullOrWhiteSpace(x.Value.Value)),
                        sheet.MergedRanges.Count);
                })
                .ToList());
    }

    public async Task<FieldInventoryPreviewResponse> CreatePreviewAsync(FieldInventoryPreviewRequest request, CorrelationContext context)
    {
        var normalizedUrl = GoogleSheetUrlParser.NormalizeWorkbookUrl(request.SourceWorkbookUrl);
        var selectedTabs = (request.SelectedTabs ?? new List<FieldInventorySelectedTab>())
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.TabName))
            .Select(CloneTabSelection)
            .ToList();

        if (selectedTabs.Count == 0)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST,
                "Select at least one workbook tab to parse.");
        }

        var run = new FieldInventoryImportRunEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            LeagueId = context.LeagueId,
            SourceWorkbookUrl = normalizedUrl,
            SourceWorkbookTitle = "",
            SeasonLabel = (request.SeasonLabel ?? "").Trim(),
            SelectedTabs = selectedTabs,
            Status = FieldInventoryImportStatuses.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = context.UserId,
        };

        return await BuildAndPersistPreviewAsync(run, context);
    }

    public async Task<FieldInventoryPreviewResponse?> GetRunAsync(string runId, CorrelationContext context)
    {
        var run = await _repository.GetImportRunAsync(context.LeagueId, runId);
        return run is null ? null : await BuildResponseAsync(run, null);
    }

    public async Task<FieldInventoryPreviewResponse> StageRunAsync(string runId, CorrelationContext context)
    {
        var run = await RequireRunAsync(runId, context);
        run.Status = FieldInventoryImportStatuses.Staged;
        run.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.UpsertImportRunAsync(run);
        return await BuildResponseAsync(run, null);
    }

    public async Task<FieldInventoryPreviewResponse> SaveFieldAliasAsync(FieldInventoryAliasSaveRequest request, CorrelationContext context)
    {
        var rawFieldName = (request.RawFieldName ?? "").Trim();
        var canonicalFieldId = (request.CanonicalFieldId ?? "").Trim();
        var canonicalFieldName = (request.CanonicalFieldName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(rawFieldName) || string.IsNullOrWhiteSpace(canonicalFieldId) || string.IsNullOrWhiteSpace(canonicalFieldName))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.FIELD_ALIAS_REQUIRED,
                "rawFieldName, canonicalFieldId, and canonicalFieldName are required.");
        }

        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertFieldAliasAsync(new FieldInventoryFieldAliasEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            LeagueId = context.LeagueId,
            RawFieldName = rawFieldName,
            NormalizedLookupKey = NormalizeLookupKey(rawFieldName),
            CanonicalFieldId = canonicalFieldId,
            CanonicalFieldName = canonicalFieldName,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = context.UserId,
        });

        var run = await RequireRunAsync(request.RunId, context);
        return await BuildAndPersistPreviewAsync(run, context);
    }

    public async Task<FieldInventoryPreviewResponse> SaveTabClassificationAsync(FieldInventoryTabClassificationSaveRequest request, CorrelationContext context)
    {
        var rawTabName = (request.RawTabName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(rawTabName))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "rawTabName is required.");
        }

        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertTabClassificationAsync(new FieldInventoryTabClassificationEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            LeagueId = context.LeagueId,
            WorkbookTitlePattern = (request.WorkbookTitlePattern ?? "").Trim(),
            RawTabName = rawTabName,
            ParserType = NormalizeParserType(request.ParserType),
            ActionType = NormalizeActionType(request.ActionType),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var run = await RequireRunAsync(request.RunId, context);
        return await BuildAndPersistPreviewAsync(run, context);
    }

    public async Task<FieldInventoryPreviewResponse> UpdateReviewItemAsync(string runId, string reviewItemId, FieldInventoryReviewDecisionRequest request, CorrelationContext context)
    {
        var run = await RequireRunAsync(runId, context);
        var reviewItems = await _repository.GetReviewItemsAsync(runId);
        var reviewItem = reviewItems.FirstOrDefault(x => string.Equals(x.Id, reviewItemId, StringComparison.OrdinalIgnoreCase));
        if (reviewItem is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.NOT_FOUND, "Review item not found.");
        }

        reviewItem.Status = NormalizeReviewItemStatus(request.Status);
        reviewItem.ChosenResolution = request.ChosenResolution ?? new Dictionary<string, string?>();
        reviewItem.SaveDecisionForFuture = request.SaveDecisionForFuture;
        reviewItem.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.SaveDecisionForFuture)
        {
            await SaveReviewDecisionForFutureAsync(run, reviewItem, context);
        }

        await _repository.UpsertReviewItemAsync(reviewItem);
        return await BuildAndPersistPreviewAsync(run, context);
    }

    public async Task<FieldInventoryPreviewResponse> CommitRunAsync(string runId, FieldInventoryCommitRequest request, CorrelationContext context)
    {
        var run = await RequireRunAsync(runId, context);
        var reviewItems = await _repository.GetReviewItemsAsync(runId);
        if (reviewItems.Any(x => x.Status == FieldInventoryReviewItemStatuses.Open && x.Severity == FieldInventoryReviewItemSeverities.Blocking))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.Conflict, ErrorCodes.REVIEW_REQUIRED,
                "Resolve blocking review items before importing staged inventory.");
        }

        var stagedRecords = await _repository.GetStagedRecordsAsync(runId);
        var seasonLabel = string.IsNullOrWhiteSpace(run.SeasonLabel) ? "current-season" : run.SeasonLabel;
        var mappedRecords = stagedRecords.Where(x => !string.IsNullOrWhiteSpace(x.FieldId)).ToList();
        var liveRecords = await _repository.GetLiveRecordsAsync(context.LeagueId, seasonLabel);

        var stagedByKey = mappedRecords.ToDictionary(BuildInventoryIdentity, StringComparer.OrdinalIgnoreCase);
        var liveByKey = liveRecords.ToDictionary(BuildInventoryIdentity, StringComparer.OrdinalIgnoreCase);
        var createCount = stagedByKey.Keys.Except(liveByKey.Keys, StringComparer.OrdinalIgnoreCase).Count();
        var unchangedCount = stagedByKey.Keys.Intersect(liveByKey.Keys, StringComparer.OrdinalIgnoreCase)
            .Count(key => LiveRecordMatches(stagedByKey[key], liveByKey[key]));
        var updateCount = stagedByKey.Keys.Intersect(liveByKey.Keys, StringComparer.OrdinalIgnoreCase).Count() - unchangedCount;
        var mode = NormalizeCommitMode(request.Mode);
        var deleteCount = mode == FieldInventoryCommitModes.Upsert && request.ReplaceExistingSeason
            ? liveByKey.Keys.Except(stagedByKey.Keys, StringComparer.OrdinalIgnoreCase).Count()
            : 0;

        var commitPreview = new FieldInventoryCommitPreviewDto(
            mode,
            request.DryRun,
            createCount,
            updateCount,
            deleteCount,
            unchangedCount,
            stagedRecords.Count - mappedRecords.Count,
            seasonLabel);

        if (!request.DryRun)
        {
            var liveToPersist = BuildCommittedLiveRecords(run, mappedRecords, liveRecords, seasonLabel, mode, request.ReplaceExistingSeason);
            await _repository.ReplaceLiveRecordsAsync(context.LeagueId, seasonLabel, liveToPersist);
            run.Status = FieldInventoryImportStatuses.Imported;
            run.UpdatedAt = DateTimeOffset.UtcNow;
            run.SummaryCounts = run.SummaryCounts with
            {
                ImportedRecords = liveToPersist.Count,
                SkippedRecords = stagedRecords.Count - mappedRecords.Count
            };
            await _repository.UpsertImportRunAsync(run);
        }

        await _repository.AddCommitRunAsync(new FieldInventoryCommitRunEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            LeagueId = context.LeagueId,
            ImportRunId = runId,
            SeasonLabel = seasonLabel,
            Mode = mode,
            DryRun = request.DryRun,
            CreateCount = createCount,
            UpdateCount = updateCount,
            DeleteCount = deleteCount,
            UnchangedCount = unchangedCount,
            SkippedUnmappedCount = stagedRecords.Count - mappedRecords.Count,
            CreatedBy = context.UserId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return await BuildResponseAsync(run, commitPreview);
    }

    private async Task<FieldInventoryPreviewResponse> BuildAndPersistPreviewAsync(FieldInventoryImportRunEntity run, CorrelationContext context)
    {
        var workbook = await LoadWorkbookAsync(run.SourceWorkbookUrl);
        run.SourceWorkbookTitle = workbook.Title;
        if (string.IsNullOrWhiteSpace(run.SeasonLabel))
        {
            run.SeasonLabel = InferSeasonLabel(workbook, run.SelectedTabs);
        }

        var selectedTabs = NormalizeSelectedTabs(run.SelectedTabs, workbook);
        run.SelectedTabs = selectedTabs;

        var aliases = await _repository.GetFieldAliasesAsync(context.LeagueId);
        var classifications = await _repository.GetTabClassificationsAsync(context.LeagueId);
        var fieldCatalog = await LoadCanonicalFieldCatalogAsync(context.LeagueId);
        var results = new ParsedSheetResult();

        for (var index = 0; index < selectedTabs.Count; index++)
        {
            var selectedTab = selectedTabs[index];
            if (!selectedTab.Selected) continue;
            var sheet = workbook.Sheets.FirstOrDefault(x => string.Equals(x.Name, selectedTab.TabName, StringComparison.OrdinalIgnoreCase));
            if (sheet is null)
            {
                results.Warnings.Add(CreateWarning(run.Id, "missing_tab", $"Selected tab '{selectedTab.TabName}' was not found in the workbook.", selectedTab.TabName ?? "", ""));
                results.ReviewItems.Add(CreateReviewItem(
                    run.Id,
                    FieldInventoryReviewItemTypes.TabClassification,
                    FieldInventoryReviewItemSeverities.Blocking,
                    $"Tab missing: {selectedTab.TabName}",
                    "A selected tab is no longer present in the workbook.",
                    selectedTab.TabName ?? "",
                    "",
                    selectedTab.TabName ?? "",
                    new Dictionary<string, string?> { ["actionType"] = FieldInventoryActionTypes.Ignore, ["parserType"] = FieldInventoryParserTypes.Ignore }));
                continue;
            }

            var decision = ResolveSelectedTabDecision(selectedTab, workbook.Title, classifications);
            selectedTabs[index] = new FieldInventorySelectedTab(sheet.Name, decision.ParserType, decision.ActionType, true);

            if (decision.ActionType == FieldInventoryActionTypes.Ignore)
            {
                if (decision.Confidence == FieldInventoryConfidence.Low)
                {
                    results.ReviewItems.Add(CreateReviewItem(
                        run.Id,
                        FieldInventoryReviewItemTypes.TabClassification,
                        FieldInventoryReviewItemSeverities.NonBlocking,
                        $"Classify tab '{sheet.Name}'",
                        "The parser could not confidently classify this tab. Save a parser decision if you want to reuse it in future runs.",
                        sheet.Name,
                        "",
                        sheet.Name,
                        new Dictionary<string, string?> { ["parserType"] = FieldInventoryParserTypes.Ignore, ["actionType"] = FieldInventoryActionTypes.Ignore }));
                }
                continue;
            }

            if (decision.ActionType == FieldInventoryActionTypes.Reference)
            {
                results.Warnings.Add(CreateWarning(
                    run.Id,
                    "reference_tab",
                    $"Reference tab '{sheet.Name}' was loaded for visibility only and did not modify staged inventory.",
                    sheet.Name,
                    ""));
                continue;
            }

            var parsed = ParseInventorySheet(run, workbook, sheet, decision.ParserType, fieldCatalog, aliases);
            results.Records.AddRange(parsed.Records);
            results.Warnings.AddRange(parsed.Warnings);
            results.ReviewItems.AddRange(parsed.ReviewItems);
        }

        run.Status = run.Status == FieldInventoryImportStatuses.Staged
            ? FieldInventoryImportStatuses.Staged
            : FieldInventoryImportStatuses.Parsed;
        run.UpdatedAt = DateTimeOffset.UtcNow;

        var unmappedFields = results.Records
            .Where(x => string.IsNullOrWhiteSpace(x.FieldId))
            .Select(x => x.RawFieldName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        run.SummaryCounts = new FieldInventorySummaryCountsDto(
            results.Records.Count,
            results.Warnings.Count,
            results.ReviewItems.Count,
            unmappedFields.Count,
            selectedTabs.Count(x => x.Selected),
            run.SummaryCounts.ImportedRecords,
            run.SummaryCounts.SkippedRecords);

        await _repository.UpsertImportRunAsync(run);
        await _repository.ReplaceRunDataAsync(run.Id, results.Records, results.Warnings, results.ReviewItems);

        return await BuildResponseAsync(run, null);
    }

    private async Task<FieldInventoryPreviewResponse> BuildResponseAsync(
        FieldInventoryImportRunEntity run,
        FieldInventoryCommitPreviewDto? commitPreview)
    {
        var records = await _repository.GetStagedRecordsAsync(run.Id);
        var warnings = await _repository.GetWarningsAsync(run.Id);
        var reviewItems = await _repository.GetReviewItemsAsync(run.Id);
        var fieldCatalog = await LoadCanonicalFieldCatalogAsync(run.LeagueId);

        return new FieldInventoryPreviewResponse(
            MapRun(run),
            records.Select(MapRecord).ToList(),
            warnings.Select(MapWarning).ToList(),
            reviewItems.Select(MapReviewItem).ToList(),
            fieldCatalog.Options,
            records.Where(x => string.IsNullOrWhiteSpace(x.FieldId))
                .Select(x => x.RawFieldName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            commitPreview);
    }

    private async Task<FieldInventoryImportRunEntity> RequireRunAsync(string? runId, CorrelationContext context)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "runId is required.");
        }

        var run = await _repository.GetImportRunAsync(context.LeagueId, runId);
        if (run is null)
        {
            throw new ApiGuards.HttpError((int)HttpStatusCode.NotFound, ErrorCodes.NOT_FOUND, "Import run not found.");
        }

        return run;
    }

    private async Task<ParsedWorkbook> LoadWorkbookAsync(string normalizedUrl)
    {
        try
        {
            return await _workbookConnector.LoadWorkbookAsync(normalizedUrl);
        }
        catch (ApiGuards.HttpError)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workbook load failed for {Url}", normalizedUrl);
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadGateway, ErrorCodes.WORKBOOK_LOAD_FAILED,
                "Unable to load the Google Sheets workbook.");
        }
    }

    private static List<FieldInventorySelectedTab> NormalizeSelectedTabs(List<FieldInventorySelectedTab> selectedTabs, ParsedWorkbook workbook)
    {
        if (selectedTabs.Count > 0)
        {
            return selectedTabs
                .Where(x => x.Selected && !string.IsNullOrWhiteSpace(x.TabName))
                .Select(CloneTabSelection)
                .ToList();
        }

        return workbook.Sheets.Select(sheet => new FieldInventorySelectedTab(sheet.Name, null, null, true)).ToList();
    }

    private static FieldInventorySelectedTab CloneTabSelection(FieldInventorySelectedTab input)
        => new(
            (input.TabName ?? "").Trim(),
            string.IsNullOrWhiteSpace(input.ParserType) ? null : input.ParserType.Trim(),
            string.IsNullOrWhiteSpace(input.ActionType) ? null : input.ActionType.Trim(),
            input.Selected);

    private TabClassificationDecision ResolveSelectedTabDecision(
        FieldInventorySelectedTab tab,
        string workbookTitle,
        List<FieldInventoryTabClassificationEntity> savedClassifications)
    {
        if (!string.IsNullOrWhiteSpace(tab.ParserType) || !string.IsNullOrWhiteSpace(tab.ActionType))
        {
            return new TabClassificationDecision(
                NormalizeParserType(tab.ParserType),
                NormalizeActionType(tab.ActionType),
                FieldInventoryConfidence.High,
                "User selection");
        }

        return InferTabClassification(new ParsedWorkbookSheet { Name = tab.TabName ?? "" }, workbookTitle, savedClassifications);
    }

    private static string InferSeasonLabel(ParsedWorkbook workbook, List<FieldInventorySelectedTab> selectedTabs)
    {
        var candidates = selectedTabs.Where(x => x.Selected).Select(x => x.TabName ?? "").Concat(new[] { workbook.Title });
        foreach (var candidate in candidates)
        {
            var match = Regex.Match(candidate, @"(?<label>(Spring|Summer|Fall|Winter)[^0-9]*(20\d{2})?)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups["label"].Value.Trim();
            }
        }

        return workbook.Title;
    }

    private ParsedSheetResult ParseInventorySheet(
        FieldInventoryImportRunEntity run,
        ParsedWorkbook workbook,
        ParsedWorkbookSheet sheet,
        string parserType,
        CanonicalFieldCatalog fieldCatalog,
        List<FieldInventoryFieldAliasEntity> aliases)
    {
        var result = new ParsedSheetResult();
        var yearHint = GetYearHint(run, workbook);
        var dateColumn = DetectDateColumn(sheet, yearHint);
        var dateRows = Enumerable.Range(1, sheet.MaxRow)
            .Where(row => dateColumn > 0 && TryParseSheetDate(GetCellValue(sheet, row, dateColumn), yearHint, out _))
            .ToList();

        if (dateRows.Count == 0)
        {
            result.Warnings.Add(CreateWarning(run.Id, "tab_layout_unknown",
                $"Selected tab '{sheet.Name}' did not match the expected grid layout. SportsCH looked for date values in the first three columns and could not find any.", sheet.Name, ""));
            result.ReviewItems.Add(CreateReviewItem(
                run.Id,
                FieldInventoryReviewItemTypes.TabClassification,
                FieldInventoryReviewItemSeverities.Blocking,
                $"Unknown layout in {sheet.Name}",
                "The parser could not find date rows in the first three columns for this tab. If this is not an AGSA inventory grid, mark it ignore or reference. If it is an inventory grid, confirm the sheet has real date values on the left side and not a different orientation.",
                sheet.Name,
                "",
                sheet.Name,
                new Dictionary<string, string?> { ["parserType"] = parserType, ["actionType"] = FieldInventoryActionTypes.Ingest }));
            return result;
        }

        var timeHeaderRow = DetectTimeHeaderRow(sheet, dateRows.Min(), dateColumn);
        if (timeHeaderRow == 0)
        {
            result.Warnings.Add(CreateWarning(run.Id, "time_header_missing",
                $"Tab '{sheet.Name}' is missing recognizable time headers. SportsCH expects headers like 5:30 PM, 17:30, or 5:30-7:00 above the inventory grid.", sheet.Name, ""));
            result.ReviewItems.Add(CreateReviewItem(
                run.Id,
                FieldInventoryReviewItemTypes.AmbiguousParse,
                FieldInventoryReviewItemSeverities.Blocking,
                $"Missing time headers in {sheet.Name}",
                "The parser found date rows but could not determine slot times. Confirm the tab has time headers above the grid and that each time cell exports as a real time or time range such as 5:30-7:00.",
                sheet.Name,
                "",
                sheet.Name,
                new Dictionary<string, string?>()));
            return result;
        }

        var timeByColumn = BuildTimeHeaderMap(sheet, timeHeaderRow, dateColumn);
        var mergedAnchors = BuildMergedAnchorMap(sheet);

        foreach (var row in dateRows)
        {
            if (!TryParseSheetDate(GetCellValue(sheet, row, dateColumn), yearHint, out var parsedDate))
            {
                continue;
            }

            for (var column = dateColumn + 1; column <= sheet.MaxColumn; column++)
            {
                var rawValue = GetMergedAwareCellValue(sheet, mergedAnchors, row, column);
                if (string.IsNullOrWhiteSpace(rawValue) || !IsAnchorCell(mergedAnchors, row, column)) continue;
                if (!timeByColumn.TryGetValue(column, out var slotTime)) continue;

                var endColumn = GetMergedEndColumn(mergedAnchors, row, column);
                var startTime = slotTime.StartTime;
                var endTime = ResolveEndTime(timeByColumn, slotTime, column, endColumn);
                var sourceRange = GetMergedSourceRange(mergedAnchors, row, column);
                var rawFieldName = ResolveFieldName(sheet, mergedAnchors, column, timeHeaderRow, dateColumn);

                if (string.IsNullOrWhiteSpace(rawFieldName))
                {
                    result.ReviewItems.Add(CreateReviewItem(
                        run.Id,
                        FieldInventoryReviewItemTypes.AmbiguousParse,
                        FieldInventoryReviewItemSeverities.NonBlocking,
                        $"Field header missing in {sheet.Name}",
                        $"Cell {sourceRange} contains inventory text but no recognizable field header.",
                        sheet.Name,
                        sourceRange,
                        rawValue,
                        new Dictionary<string, string?>()));
                    continue;
                }

                var status = InferStatus(rawValue);
                if (!status.HasMeaningfulInventory) continue;

                var resolution = ResolveFieldAlias(rawFieldName, fieldCatalog, aliases);
                var warningFlags = new List<string>();
                if (!resolution.IsMapped) warningFlags.Add("unmapped_field");
                if (status.IsExternalUsage) warningFlags.Add("external_usage");
                if (status.Confidence == FieldInventoryConfidence.Low) warningFlags.Add("low_confidence");

                var record = new FieldInventoryStagedRecordEntity
                {
                    Id = BuildRecordId(sheet.Name, sourceRange, rawFieldName, parsedDate, startTime),
                    ImportRunId = run.Id,
                    LeagueId = run.LeagueId,
                    FieldId = resolution.FieldId,
                    FieldName = resolution.FieldName,
                    RawFieldName = rawFieldName,
                    Date = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DayOfWeek = parsedDate.DayOfWeek.ToString(),
                    StartTime = startTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    EndTime = endTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    SlotDurationMinutes = (int)(endTime - startTime).TotalMinutes,
                    AvailabilityStatus = status.AvailabilityStatus,
                    UtilizationStatus = status.UtilizationStatus,
                    UsageType = status.UsageType,
                    UsedBy = status.UsedBy,
                    AssignedGroup = status.AssignedGroup,
                    AssignedDivision = status.AssignedDivision,
                    AssignedTeamOrEvent = status.AssignedTeamOrEvent,
                    SourceWorkbookUrl = run.SourceWorkbookUrl,
                    SourceTab = sheet.Name,
                    SourceCellRange = sourceRange,
                    SourceValue = rawValue,
                    SourceColor = GetMergedAwareBackgroundColor(sheet, mergedAnchors, row, column),
                    ParserType = parserType,
                    Confidence = CombineConfidence(status.Confidence, resolution.Confidence),
                    WarningFlags = warningFlags,
                    ReviewStatus = !resolution.IsMapped || status.Confidence == FieldInventoryConfidence.Low
                        ? FieldInventoryReviewStatuses.NeedsReview
                        : FieldInventoryReviewStatuses.None,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };

                result.Records.Add(record);

                if (!resolution.IsMapped)
                {
                    result.ReviewItems.Add(CreateReviewItem(
                        run.Id,
                        FieldInventoryReviewItemTypes.FieldMapping,
                        FieldInventoryReviewItemSeverities.NonBlocking,
                        $"Map field '{rawFieldName}'",
                        $"The sheet field '{rawFieldName}' is not yet mapped to a canonical SportsCH field.",
                        sheet.Name,
                        sourceRange,
                        rawFieldName,
                        new Dictionary<string, string?>()));
                }

                if (record.Confidence == FieldInventoryConfidence.Low)
                {
                    result.ReviewItems.Add(CreateReviewItem(
                        run.Id,
                        FieldInventoryReviewItemTypes.LowConfidence,
                        FieldInventoryReviewItemSeverities.NonBlocking,
                        $"Review low-confidence record {sourceRange}",
                        "The parser created a record but could not classify all metadata with high confidence.",
                        sheet.Name,
                        sourceRange,
                        rawValue,
                        new Dictionary<string, string?>()));
                }
            }
        }

        result.ReviewItems = result.ReviewItems
            .GroupBy(x => $"{x.ItemType}|{x.SourceTab}|{x.SourceCellRange}|{x.RawValue}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        return result;
    }

    private async Task SaveReviewDecisionForFutureAsync(FieldInventoryImportRunEntity run, FieldInventoryReviewQueueItemEntity reviewItem, CorrelationContext context)
    {
        if (reviewItem.ItemType == FieldInventoryReviewItemTypes.FieldMapping
            && reviewItem.ChosenResolution.TryGetValue("canonicalFieldId", out var fieldId)
            && reviewItem.ChosenResolution.TryGetValue("canonicalFieldName", out var fieldName)
            && !string.IsNullOrWhiteSpace(fieldId)
            && !string.IsNullOrWhiteSpace(fieldName))
        {
            await _repository.UpsertFieldAliasAsync(new FieldInventoryFieldAliasEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                LeagueId = context.LeagueId,
                RawFieldName = reviewItem.RawValue,
                NormalizedLookupKey = NormalizeLookupKey(reviewItem.RawValue),
                CanonicalFieldId = fieldId.Trim(),
                CanonicalFieldName = fieldName.Trim(),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedBy = context.UserId,
            });
        }

        if (reviewItem.ItemType == FieldInventoryReviewItemTypes.TabClassification
            && reviewItem.ChosenResolution.TryGetValue("parserType", out var parserType)
            && reviewItem.ChosenResolution.TryGetValue("actionType", out var actionType))
        {
            await _repository.UpsertTabClassificationAsync(new FieldInventoryTabClassificationEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                LeagueId = context.LeagueId,
                WorkbookTitlePattern = run.SourceWorkbookTitle,
                RawTabName = reviewItem.SourceTab,
                ParserType = NormalizeParserType(parserType),
                ActionType = NormalizeActionType(actionType),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    private async Task<CanonicalFieldCatalog> LoadCanonicalFieldCatalogAsync(string leagueId)
    {
        var entities = await _fieldRepository.QueryFieldsAsync(leagueId);
        var options = new List<CanonicalFieldOptionDto>();
        var entries = new List<CanonicalFieldEntry>();
        var byLookupKey = new Dictionary<string, CanonicalFieldEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var parkCode = ExtractParkCode(entity.PartitionKey, leagueId);
            var fieldCode = entity.RowKey;
            var fieldId = $"{parkCode}/{fieldCode}";
            var parkName = (entity.GetString("ParkName") ?? "").Trim();
            var fieldName = (entity.GetString("FieldName") ?? "").Trim();
            var displayName = (entity.GetString("DisplayName") ?? "").Trim();
            var canonicalFieldName = string.IsNullOrWhiteSpace(displayName)
                ? $"{parkName} > {fieldName}".Trim(' ', '>')
                : displayName;
            var entry = new CanonicalFieldEntry(fieldId, canonicalFieldName, fieldName, parkName);
            entries.Add(entry);
            options.Add(new CanonicalFieldOptionDto(fieldId, canonicalFieldName, fieldName, parkName));

            foreach (var key in entry.LookupKeys)
            {
                if (!byLookupKey.ContainsKey(key))
                {
                    byLookupKey[key] = entry;
                }
            }
        }

        return new CanonicalFieldCatalog(options.OrderBy(x => x.CanonicalFieldName).ToList(), entries, byLookupKey);
    }

    private static string ExtractParkCode(string partitionKey, string leagueId)
    {
        var prefix = $"FIELD|{leagueId}|";
        return partitionKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? partitionKey[prefix.Length..]
            : "";
    }

    private static TabClassificationDecision InferTabClassification(
        ParsedWorkbookSheet sheet,
        string workbookTitle,
        List<FieldInventoryTabClassificationEntity> savedClassifications)
    {
        var tabName = sheet.Name;
        var saved = savedClassifications.FirstOrDefault(x =>
            string.Equals(x.RawTabName, tabName, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(x.WorkbookTitlePattern)
                || workbookTitle.Contains(x.WorkbookTitlePattern, StringComparison.OrdinalIgnoreCase)));
        if (saved is not null)
        {
            return new TabClassificationDecision(saved.ParserType, saved.ActionType, FieldInventoryConfidence.High, "Saved classification");
        }

        if (sheet.IsHidden)
        {
            return new TabClassificationDecision(FieldInventoryParserTypes.Ignore, FieldInventoryActionTypes.Ignore, FieldInventoryConfidence.High, "Hidden workbook tab");
        }

        var normalized = NormalizeLookupKey(tabName);
        if (normalized.Contains("requestform"))
        {
            return new TabClassificationDecision(FieldInventoryParserTypes.Ignore, FieldInventoryActionTypes.Ignore, FieldInventoryConfidence.High, "Request form tab");
        }

        if (IsAgsaWeekendInventoryTab(tabName))
        {
            return new TabClassificationDecision(FieldInventoryParserTypes.WeekendGrid, FieldInventoryActionTypes.Ingest, FieldInventoryConfidence.High, "AGSA weekend inventory tab");
        }

        if (IsAgsaWeekdayInventoryTab(tabName))
        {
            return new TabClassificationDecision(FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, FieldInventoryConfidence.High, "AGSA weekday inventory tab");
        }

        if (normalized.Contains("countygrid"))
        {
            return new TabClassificationDecision(FieldInventoryParserTypes.Ignore, FieldInventoryActionTypes.Ignore, FieldInventoryConfidence.High, "Reference tab ignored by default");
        }

        return new TabClassificationDecision(FieldInventoryParserTypes.Ignore, FieldInventoryActionTypes.Ignore, FieldInventoryConfidence.Medium, "Non-AGSA support tab ignored by default");
    }

    private static bool IsAgsaWeekendInventoryTab(string tabName)
        => string.Equals((tabName ?? "").Trim(), "Weekends", StringComparison.OrdinalIgnoreCase);

    private static bool IsAgsaWeekdayInventoryTab(string tabName)
        => Regex.IsMatch((tabName ?? "").Trim(),
            @"^(Spring|Summer|Fall|Winter)\s+\d{1,2}(?:[/-]\d{1,2})?\s*-\s*\d{1,2}(?:[/-]\d{1,2})?$",
            RegexOptions.IgnoreCase);

    private static string NormalizeLookupKey(string? value)
        => Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"[^a-z0-9]+", "");

    private static string NormalizeParserType(string? parserType)
        => parserType switch
        {
            FieldInventoryParserTypes.WeekendGrid => FieldInventoryParserTypes.WeekendGrid,
            FieldInventoryParserTypes.ReferenceGrid => FieldInventoryParserTypes.ReferenceGrid,
            FieldInventoryParserTypes.Ignore => FieldInventoryParserTypes.Ignore,
            _ => FieldInventoryParserTypes.SeasonWeekdayGrid
        };

    private static string NormalizeActionType(string? actionType)
        => actionType switch
        {
            FieldInventoryActionTypes.Reference => FieldInventoryActionTypes.Reference,
            FieldInventoryActionTypes.Ignore => FieldInventoryActionTypes.Ignore,
            _ => FieldInventoryActionTypes.Ingest
        };

    private static string NormalizeReviewItemStatus(string? status)
        => status switch
        {
            FieldInventoryReviewItemStatuses.Resolved => FieldInventoryReviewItemStatuses.Resolved,
            FieldInventoryReviewItemStatuses.Ignored => FieldInventoryReviewItemStatuses.Ignored,
            _ => FieldInventoryReviewItemStatuses.Open
        };

    private static string NormalizeCommitMode(string? mode)
        => string.Equals(mode, FieldInventoryCommitModes.Import, StringComparison.OrdinalIgnoreCase)
            ? FieldInventoryCommitModes.Import
            : FieldInventoryCommitModes.Upsert;

    private static int GetYearHint(FieldInventoryImportRunEntity run, ParsedWorkbook workbook)
    {
        foreach (var candidate in new[] { run.SeasonLabel, workbook.Title })
        {
            var match = Regex.Match(candidate ?? "", @"20\d{2}");
            if (match.Success && int.TryParse(match.Value, out var year))
            {
                return year;
            }
        }

        return DateTime.UtcNow.Year;
    }

    private static int DetectDateColumn(ParsedWorkbookSheet sheet, int yearHint)
    {
        var bestColumn = 0;
        var bestCount = 0;
        var lastDateRow = 0;
        for (var column = 1; column <= Math.Min(3, sheet.MaxColumn); column++)
        {
            var count = 0;
            var currentLastRow = 0;
            for (var row = 1; row <= sheet.MaxRow; row++)
            {
                if (!TryParseSheetDate(GetCellValue(sheet, row, column), yearHint, out _))
                {
                    continue;
                }

                count += 1;
                currentLastRow = row;
            }

            if (count > bestCount || (count == bestCount && currentLastRow > lastDateRow))
            {
                bestCount = count;
                bestColumn = column;
                lastDateRow = currentLastRow;
            }
        }

        return bestCount > 0 ? bestColumn : 0;
    }

    private static int DetectTimeHeaderRow(ParsedWorkbookSheet sheet, int firstDateRow, int dateColumn)
    {
        var bestRow = 0;
        var bestCount = 0;
        for (var row = 1; row < firstDateRow; row++)
        {
            var count = 0;
            for (var column = dateColumn + 1; column <= sheet.MaxColumn; column++)
            {
                if (TryParseSheetTimeHeader(GetCellValue(sheet, row, column), out _))
                {
                    count += 1;
                }
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestRow = row;
            }
        }

        return bestCount > 0 ? bestRow : 0;
    }

    private static Dictionary<int, TimeHeaderSlot> BuildTimeHeaderMap(ParsedWorkbookSheet sheet, int timeHeaderRow, int dateColumn)
    {
        var output = new Dictionary<int, TimeHeaderSlot>();
        for (var column = dateColumn + 1; column <= sheet.MaxColumn; column++)
        {
            if (TryParseSheetTimeHeader(GetCellValue(sheet, timeHeaderRow, column), out var slot))
            {
                output[column] = slot;
            }
        }
        return output;
    }

    private static Dictionary<(int row, int column), ParsedWorkbookMergedRange> BuildMergedAnchorMap(ParsedWorkbookSheet sheet)
    {
        var map = new Dictionary<(int row, int column), ParsedWorkbookMergedRange>();
        foreach (var merge in sheet.MergedRanges)
        {
            for (var row = merge.StartRow; row <= merge.EndRow; row++)
            {
                for (var column = merge.StartColumn; column <= merge.EndColumn; column++)
                {
                    map[(row, column)] = merge;
                }
            }
        }
        return map;
    }

    private static bool IsAnchorCell(Dictionary<(int row, int column), ParsedWorkbookMergedRange> mergedAnchors, int row, int column)
        => !mergedAnchors.TryGetValue((row, column), out var merge)
           || (merge.StartRow == row && merge.StartColumn == column);

    private static int GetMergedEndColumn(Dictionary<(int row, int column), ParsedWorkbookMergedRange> mergedAnchors, int row, int column)
        => mergedAnchors.TryGetValue((row, column), out var merge) ? merge.EndColumn : column;

    private static string GetMergedSourceRange(Dictionary<(int row, int column), ParsedWorkbookMergedRange> mergedAnchors, int row, int column)
        => mergedAnchors.TryGetValue((row, column), out var merge) ? merge.Reference : $"{ColumnName(column)}{row}";

    private static string? GetMergedAwareBackgroundColor(ParsedWorkbookSheet sheet, Dictionary<(int row, int column), ParsedWorkbookMergedRange> mergedAnchors, int row, int column)
    {
        if (mergedAnchors.TryGetValue((row, column), out var merge))
        {
            return GetCell(sheet, merge.StartRow, merge.StartColumn)?.BackgroundColor;
        }

        return GetCell(sheet, row, column)?.BackgroundColor;
    }

    private static string GetMergedAwareCellValue(ParsedWorkbookSheet sheet, Dictionary<(int row, int column), ParsedWorkbookMergedRange> mergedAnchors, int row, int column)
    {
        if (mergedAnchors.TryGetValue((row, column), out var merge))
        {
            return GetCellValue(sheet, merge.StartRow, merge.StartColumn);
        }

        return GetCellValue(sheet, row, column);
    }

    private static string ResolveFieldName(
        ParsedWorkbookSheet sheet,
        Dictionary<(int row, int column), ParsedWorkbookMergedRange> mergedAnchors,
        int column,
        int timeHeaderRow,
        int dateColumn)
    {
        for (var row = timeHeaderRow - 1; row >= 1; row--)
        {
            var candidate = GetMergedAwareCellValue(sheet, mergedAnchors, row, column).Trim();
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (TryParseSheetTime(candidate, out _)) continue;
            if (TryParseSheetDate(candidate, DateTime.UtcNow.Year, out _)) continue;
            if (column == dateColumn) continue;
            return candidate;
        }

        return "";
    }

    private static TimeOnly ResolveEndTime(Dictionary<int, TimeHeaderSlot> timeByColumn, TimeHeaderSlot slot, int startColumn, int endColumn)
    {
        if (slot.EndTime.HasValue && slot.EndTime.Value > slot.StartTime && endColumn == startColumn)
        {
            return slot.EndTime.Value;
        }

        if (timeByColumn.TryGetValue(endColumn + 1, out var nextSlot) && nextSlot.StartTime > slot.StartTime)
        {
            return nextSlot.StartTime;
        }

        if (timeByColumn.TryGetValue(startColumn + 1, out var singleNext) && singleNext.StartTime > slot.StartTime)
        {
            var stepMinutes = (int)(singleNext.StartTime - slot.StartTime).TotalMinutes;
            return slot.StartTime.AddMinutes(stepMinutes * Math.Max(1, endColumn - startColumn + 1));
        }

        return slot.StartTime.AddMinutes(60 * Math.Max(1, endColumn - startColumn + 1));
    }

    private static bool TryParseSheetDate(string? raw, int yearHint, out DateOnly date)
    {
        date = default;
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;

        value = Regex.Replace(value, @"\b(Mon(day)?|Tue(s(day)?)?|Wed(nesday)?|Thu(r(s(day)?)?)?|Fri(day)?|Sat(urday)?|Sun(day)?)\b", "", RegexOptions.IgnoreCase).Trim();
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)
            || (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDateTime)
                && (date = DateOnly.FromDateTime(parsedDateTime)) != default)
            || (Regex.IsMatch(value, @"^\d{1,2}/\d{1,2}$")
                && DateOnly.TryParse($"{value}/{yearHint}", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            || (Regex.IsMatch(value, @"^\d{1,2}-\d{1,2}$")
                && DateOnly.TryParse(value.Replace('-', '/') + $"/{yearHint}", CultureInfo.InvariantCulture, DateTimeStyles.None, out date));
    }

    private static bool TryParseSheetTime(string? raw, out TimeOnly time)
    {
        time = default;
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;

        var formats = new[] { "H:mm", "HH:mm", "h:mm tt", "h:mmtt", "htt", "h tt" };
        if (TimeOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time))
        {
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDateTime))
        {
            time = TimeOnly.FromDateTime(parsedDateTime);
            return true;
        }

        return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time);
    }

    private static bool TryParseSheetTimeHeader(string? raw, out TimeHeaderSlot slot)
    {
        slot = default;
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;

        value = value.Replace('\u2013', '-').Replace('\u2014', '-');
        var rangeMatch = Regex.Match(value, @"^(?<start>.+?)\s*-\s*(?<end>.+)$");
        if (rangeMatch.Success)
        {
            var startRaw = rangeMatch.Groups["start"].Value.Trim();
            var endRaw = rangeMatch.Groups["end"].Value.Trim();
            if (!TryParseSheetTime(startRaw, out var startTime))
            {
                var suffixMatch = Regex.Match(endRaw, @"(?i)\b(am|pm)\b");
                if (!suffixMatch.Success || !TryParseSheetTime($"{startRaw} {suffixMatch.Value}", out startTime))
                {
                    return false;
                }
            }

            if (!TryParseSheetTime(endRaw, out var endTime))
            {
                return false;
            }

            slot = new TimeHeaderSlot(startTime, endTime);
            return endTime > startTime;
        }

        if (TryParseSheetTime(value, out var singleTime))
        {
            slot = new TimeHeaderSlot(singleTime, null);
            return true;
        }

        return false;
    }

    private static ParsedWorkbookCell? GetCell(ParsedWorkbookSheet sheet, int row, int column)
        => sheet.CellsByIndex.TryGetValue((row, column), out var cell) ? cell : null;

    private static string GetCellValue(ParsedWorkbookSheet sheet, int row, int column)
        => GetCell(sheet, row, column)?.Value ?? "";

    private static FieldResolution ResolveFieldAlias(
        string rawFieldName,
        CanonicalFieldCatalog fieldCatalog,
        List<FieldInventoryFieldAliasEntity> aliases)
    {
        var lookupKey = NormalizeLookupKey(rawFieldName);
        var saved = aliases.FirstOrDefault(x => string.Equals(x.NormalizedLookupKey, lookupKey, StringComparison.OrdinalIgnoreCase));
        if (saved is not null)
        {
            return new FieldResolution(true, saved.CanonicalFieldId, saved.CanonicalFieldName, FieldInventoryConfidence.High);
        }

        if (fieldCatalog.ByLookupKey.TryGetValue(lookupKey, out var exact))
        {
            return new FieldResolution(true, exact.FieldId, exact.CanonicalFieldName, FieldInventoryConfidence.High);
        }

        var fieldNameMatches = fieldCatalog.Entries
            .Where(x => string.Equals(NormalizeLookupKey(x.FieldName), lookupKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (fieldNameMatches.Count == 1)
        {
            return new FieldResolution(true, fieldNameMatches[0].FieldId, fieldNameMatches[0].CanonicalFieldName, FieldInventoryConfidence.Medium);
        }

        return new FieldResolution(false, null, null, FieldInventoryConfidence.Low);
    }

    private static InventoryCellStatus InferStatus(string rawValue)
    {
        var value = (rawValue ?? "").Trim();
        var normalized = value.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value))
        {
            return InventoryCellStatus.None();
        }

        var division = ExtractDivision(value);
        var usageType = InferUsageType(normalized);
        var usedBy = InferUsedBy(normalized);

        if (normalized.Contains("available") || normalized == "avail" || normalized == "open")
        {
            return new InventoryCellStatus(true, FieldInventoryAvailabilityStatuses.Available, FieldInventoryUtilizationStatuses.NotUsed,
                null, "AGSA", null, division, null, FieldInventoryConfidence.High, false);
        }

        if (normalized.Contains("pending") || normalized.Contains("requested"))
        {
            return new InventoryCellStatus(true, FieldInventoryAvailabilityStatuses.Pending, FieldInventoryUtilizationStatuses.Unknown,
                usageType, "AGSA", null, division, value, FieldInventoryConfidence.Medium, false);
        }

        if (normalized.Contains("closed") || normalized.Contains("unavailable"))
        {
            return new InventoryCellStatus(true, FieldInventoryAvailabilityStatuses.Unavailable, FieldInventoryUtilizationStatuses.Unknown,
                null, usedBy, null, division, value, FieldInventoryConfidence.Medium,
                usedBy is not null && !string.Equals(usedBy, "AGSA", StringComparison.OrdinalIgnoreCase));
        }

        if (usedBy is not null || usageType is not null || Regex.IsMatch(value, @"[A-Za-z]"))
        {
            return new InventoryCellStatus(true, FieldInventoryAvailabilityStatuses.Unavailable, FieldInventoryUtilizationStatuses.Used,
                usageType ?? "other", usedBy ?? "AGSA", null, division, value,
                usageType is not null ? FieldInventoryConfidence.High : FieldInventoryConfidence.Low,
                usedBy is not null && !string.Equals(usedBy, "AGSA", StringComparison.OrdinalIgnoreCase));
        }

        return InventoryCellStatus.None();
    }

    private static string? ExtractDivision(string value)
    {
        var match = Regex.Match(value, @"\b(?:(\d{1,2})U|U(\d{1,2}))\b", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var number = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return string.IsNullOrWhiteSpace(number) ? null : $"{number}U";
    }

    private static string? InferUsageType(string normalized)
    {
        if (normalized.Contains("practice")) return "practice";
        if (normalized.Contains("game") || normalized.Contains("scrimmage")) return "game";
        if (normalized.Contains("tryout")) return "tryout";
        if (normalized.Contains("clinic")) return "clinic";
        if (normalized.Contains("camp")) return "camp";
        if (normalized.Contains("meeting")) return "meeting";
        if (normalized.Contains("hold") || normalized.Contains("blocked")) return "hold";
        return null;
    }

    private static string? InferUsedBy(string normalized)
    {
        if (normalized.Contains("county")) return "county";
        if (normalized.Contains("school") || normalized.Contains("high school") || normalized.Contains("hs")) return "school";
        if (normalized.Contains("external") || normalized.Contains("outside")) return "external";
        if (normalized.Contains("agsa")) return "AGSA";
        return null;
    }

    private static string CombineConfidence(string left, string right)
    {
        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [FieldInventoryConfidence.High] = 3,
            [FieldInventoryConfidence.Medium] = 2,
            [FieldInventoryConfidence.Low] = 1,
        };

        return ranks[left] <= ranks[right] ? left : right;
    }

    private static string BuildRecordId(string tabName, string sourceRange, string rawFieldName, DateOnly date, TimeOnly startTime)
        => Slug.Make($"{tabName}|{sourceRange}|{rawFieldName}|{date:yyyy-MM-dd}|{startTime:HH\\:mm}");

    private static FieldInventoryWarningEntity CreateWarning(string runId, string code, string message, string sourceTab, string sourceCellRange, string? relatedRecordId = null)
        => new()
        {
            Id = Guid.NewGuid().ToString("n"),
            ImportRunId = runId,
            Severity = code == "reference_tab" ? FieldInventoryWarningSeverities.Info : FieldInventoryWarningSeverities.Warning,
            Code = code,
            Message = message,
            SourceTab = sourceTab,
            SourceCellRange = sourceCellRange,
            RelatedRecordId = relatedRecordId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static FieldInventoryReviewQueueItemEntity CreateReviewItem(
        string runId,
        string itemType,
        string severity,
        string title,
        string description,
        string sourceTab,
        string sourceCellRange,
        string rawValue,
        Dictionary<string, string?> suggestedResolution)
        => new()
        {
            Id = Guid.NewGuid().ToString("n"),
            ImportRunId = runId,
            ItemType = itemType,
            Severity = severity,
            Title = title,
            Description = description,
            SourceTab = sourceTab,
            SourceCellRange = sourceCellRange,
            RawValue = rawValue,
            SuggestedResolution = suggestedResolution,
            ChosenResolution = new Dictionary<string, string?>(),
            Status = FieldInventoryReviewItemStatuses.Open,
            SaveDecisionForFuture = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static FieldInventoryRunDto MapRun(FieldInventoryImportRunEntity run)
        => new(
            run.Id,
            run.SourceWorkbookUrl,
            run.SourceWorkbookTitle,
            run.SeasonLabel,
            run.Status,
            run.SelectedTabs.Select(CloneTabSelection).ToList(),
            run.SummaryCounts,
            run.CreatedAt,
            run.UpdatedAt,
            run.CreatedBy);

    private static FieldInventoryStagedRecordDto MapRecord(FieldInventoryStagedRecordEntity record)
        => new(
            record.Id,
            record.ImportRunId,
            record.FieldId,
            record.FieldName,
            record.RawFieldName,
            record.Date,
            record.DayOfWeek,
            record.StartTime,
            record.EndTime,
            record.SlotDurationMinutes,
            record.AvailabilityStatus,
            record.UtilizationStatus,
            record.UsageType,
            record.UsedBy,
            record.AssignedGroup,
            record.AssignedDivision,
            record.AssignedTeamOrEvent,
            record.SourceWorkbookUrl,
            record.SourceTab,
            record.SourceCellRange,
            record.SourceValue,
            record.SourceColor,
            record.ParserType,
            record.Confidence,
            record.WarningFlags,
            record.ReviewStatus);

    private static FieldInventoryWarningDto MapWarning(FieldInventoryWarningEntity warning)
        => new(
            warning.Id,
            warning.ImportRunId,
            warning.Severity,
            warning.Code,
            warning.Message,
            warning.SourceTab,
            warning.SourceCellRange,
            warning.RelatedRecordId);

    private static FieldInventoryReviewItemDto MapReviewItem(FieldInventoryReviewQueueItemEntity item)
        => new(
            item.Id,
            item.ImportRunId,
            item.ItemType,
            item.Severity,
            item.Title,
            item.Description,
            item.SourceTab,
            item.SourceCellRange,
            item.RawValue,
            item.SuggestedResolution,
            item.ChosenResolution,
            item.Status,
            item.SaveDecisionForFuture);

    private static string BuildInventoryIdentity(FieldInventoryStagedRecordEntity record)
        => $"{record.FieldId}|{record.Date}|{record.StartTime}|{record.EndTime}";

    private static string BuildInventoryIdentity(FieldInventoryLiveRecordEntity record)
        => $"{record.FieldId}|{record.Date}|{record.StartTime}|{record.EndTime}";

    private static bool LiveRecordMatches(FieldInventoryStagedRecordEntity staged, FieldInventoryLiveRecordEntity live)
        => staged.FieldId == live.FieldId
            && staged.FieldName == live.FieldName
            && staged.RawFieldName == live.RawFieldName
            && staged.Date == live.Date
            && staged.StartTime == live.StartTime
            && staged.EndTime == live.EndTime
            && staged.AvailabilityStatus == live.AvailabilityStatus
            && staged.UtilizationStatus == live.UtilizationStatus
            && staged.UsageType == live.UsageType
            && staged.UsedBy == live.UsedBy
            && staged.AssignedDivision == live.AssignedDivision
            && staged.AssignedTeamOrEvent == live.AssignedTeamOrEvent
            && staged.SourceTab == live.SourceTab
            && staged.ParserType == live.ParserType;

    private static List<FieldInventoryLiveRecordEntity> BuildCommittedLiveRecords(
        FieldInventoryImportRunEntity run,
        List<FieldInventoryStagedRecordEntity> mappedRecords,
        List<FieldInventoryLiveRecordEntity> existingLiveRecords,
        string seasonLabel,
        string mode,
        bool replaceExistingSeason)
    {
        var now = DateTimeOffset.UtcNow;
        var stagedLive = mappedRecords.Select(record => new FieldInventoryLiveRecordEntity
        {
            Id = record.Id,
            LeagueId = run.LeagueId,
            SeasonLabel = seasonLabel,
            ImportRunId = run.Id,
            FieldId = record.FieldId ?? "",
            FieldName = record.FieldName ?? record.RawFieldName,
            RawFieldName = record.RawFieldName,
            Date = record.Date,
            DayOfWeek = record.DayOfWeek,
            StartTime = record.StartTime,
            EndTime = record.EndTime,
            SlotDurationMinutes = record.SlotDurationMinutes,
            AvailabilityStatus = record.AvailabilityStatus,
            UtilizationStatus = record.UtilizationStatus,
            UsageType = record.UsageType,
            UsedBy = record.UsedBy,
            AssignedGroup = record.AssignedGroup,
            AssignedDivision = record.AssignedDivision,
            AssignedTeamOrEvent = record.AssignedTeamOrEvent,
            SourceWorkbookUrl = record.SourceWorkbookUrl,
            SourceTab = record.SourceTab,
            SourceCellRange = record.SourceCellRange,
            SourceValue = record.SourceValue,
            SourceColor = record.SourceColor,
            ParserType = record.ParserType,
            Confidence = record.Confidence,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();

        if (mode == FieldInventoryCommitModes.Import)
        {
            var existingByKey = existingLiveRecords.ToDictionary(BuildInventoryIdentity, StringComparer.OrdinalIgnoreCase);
            var merged = new List<FieldInventoryLiveRecordEntity>(existingLiveRecords);
            foreach (var record in stagedLive)
            {
                if (!existingByKey.ContainsKey(BuildInventoryIdentity(record)))
                {
                    merged.Add(record);
                }
            }
            return merged;
        }

        if (replaceExistingSeason)
        {
            return stagedLive;
        }

        var output = existingLiveRecords.ToDictionary(BuildInventoryIdentity, StringComparer.OrdinalIgnoreCase);
        foreach (var record in stagedLive)
        {
            output[BuildInventoryIdentity(record)] = record;
        }
        return output.Values.ToList();
    }

    private static string ColumnName(int column)
    {
        var dividend = column;
        var name = "";
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            name = Convert.ToChar(65 + modulo) + name;
            dividend = (dividend - modulo) / 26;
        }
        return name;
    }

    public interface IFieldInventoryWorkbookConnector
    {
        Task<ParsedWorkbook> LoadWorkbookAsync(string sourceWorkbookUrl);
    }

    internal sealed class PublicGoogleSheetsWorkbookConnector : IFieldInventoryWorkbookConnector
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public PublicGoogleSheetsWorkbookConnector(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ParsedWorkbook> LoadWorkbookAsync(string sourceWorkbookUrl)
        {
            var normalizedUrl = GoogleSheetUrlParser.NormalizeWorkbookUrl(sourceWorkbookUrl);
            var spreadsheetId = GoogleSheetUrlParser.ExtractSpreadsheetId(normalizedUrl);
            using var client = _httpClientFactory.CreateClient(nameof(FieldInventoryImportService));
            HttpStatusCode? lastStatusCode = null;

            foreach (var exportUrl in GoogleSheetUrlParser.BuildWorkbookExportUrls(normalizedUrl))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, exportUrl);
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                request.Headers.TryAddWithoutValidation("Accept",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/octet-stream;q=0.9,*/*;q=0.8");
                request.Headers.Referrer = new Uri(normalizedUrl);

                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    lastStatusCode = response.StatusCode;
                    continue;
                }

                await using var responseStream = await response.Content.ReadAsStreamAsync();
                await using var workbookBuffer = new MemoryStream();
                await responseStream.CopyToAsync(workbookBuffer);
                workbookBuffer.Position = 0;

                try
                {
                    return WorkbookXlsxReader.Read(workbookBuffer, normalizedUrl, spreadsheetId);
                }
                catch (InvalidDataException)
                {
                    workbookBuffer.Position = 0;
                }
                catch (InvalidOperationException)
                {
                    workbookBuffer.Position = 0;
                }
            }

            var statusCode = (int)(lastStatusCode ?? HttpStatusCode.BadGateway);
            var message = statusCode == (int)HttpStatusCode.Unauthorized || statusCode == (int)HttpStatusCode.Forbidden
                ? $"Workbook export failed with status {statusCode}. Google Sheets usually returns this when the workbook is not shared for anonymous view or download. Set the sheet to 'Anyone with the link can view' and try again."
                : $"Workbook export failed with status {statusCode}.";
            throw new ApiGuards.HttpError((int)HttpStatusCode.BadGateway, ErrorCodes.WORKBOOK_LOAD_FAILED,
                message);
        }
    }

    public static class GoogleSheetUrlParser
    {
        private static readonly Regex SpreadsheetIdRegex = new(@"/spreadsheets/d/(?<id>[a-zA-Z0-9-_]+)", RegexOptions.Compiled);

        public static string NormalizeWorkbookUrl(string? sourceWorkbookUrl)
        {
            var trimmed = (sourceWorkbookUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_WORKBOOK_URL, "A Google Sheets workbook URL is required.");
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_WORKBOOK_URL, "The workbook URL is not valid.");
            }

            if (!string.Equals(uri.Host, "docs.google.com", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.Contains("/spreadsheets/d/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_WORKBOOK_URL,
                    "Use a public Google Sheets workbook URL in docs.google.com/spreadsheets format.");
            }

            var resourceKey = GetQueryValue(uri.Query, "resourcekey");
            var suffix = string.IsNullOrWhiteSpace(resourceKey)
                ? ""
                : $"?resourcekey={Uri.EscapeDataString(resourceKey)}";
            return $"https://docs.google.com{uri.AbsolutePath}{suffix}";
        }

        public static string ExtractSpreadsheetId(string workbookUrl)
        {
            var match = SpreadsheetIdRegex.Match(workbookUrl);
            if (!match.Success)
            {
                throw new ApiGuards.HttpError((int)HttpStatusCode.BadRequest, ErrorCodes.INVALID_WORKBOOK_URL, "The workbook URL is missing a spreadsheet ID.");
            }

            return match.Groups["id"].Value;
        }

        public static string? ExtractResourceKey(string workbookUrl)
        {
            if (!Uri.TryCreate(workbookUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            return GetQueryValue(uri.Query, "resourcekey");
        }

        public static IReadOnlyList<string> BuildWorkbookExportUrls(string workbookUrl)
        {
            var spreadsheetId = ExtractSpreadsheetId(workbookUrl);
            var resourceKey = ExtractResourceKey(workbookUrl);
            var resourceKeyQuery = string.IsNullOrWhiteSpace(resourceKey)
                ? ""
                : $"&resourcekey={Uri.EscapeDataString(resourceKey)}";

            return new List<string>
            {
                $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=xlsx&id={Uri.EscapeDataString(spreadsheetId)}{resourceKeyQuery}",
                $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?exportFormat=xlsx&format=xlsx&id={Uri.EscapeDataString(spreadsheetId)}{resourceKeyQuery}",
                $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=xlsx{resourceKeyQuery}",
                $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/pub?output=xlsx"
            };
        }

        private static string? GetQueryValue(string query, string key)
        {
            var trimmed = (query ?? "").TrimStart('?');
            if (string.IsNullOrWhiteSpace(trimmed)) return null;

            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=', 2);
                var currentKey = Uri.UnescapeDataString(pieces[0] ?? "");
                if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1] ?? "") : "";
            }

            return null;
        }
    }

    internal static class WorkbookXlsxReader
    {
        internal static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

        public static ParsedWorkbook Read(Stream stream, string sourceWorkbookUrl, string spreadsheetId)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidOperationException("Workbook XML missing.");
            var workbookDocument = XDocument.Load(workbookEntry.Open());
            var workbookRelationships = LoadWorkbookRelationships(archive);
            var sharedStrings = LoadSharedStrings(archive);
            var styles = WorkbookStyleContext.Load(archive);
            var title = LoadWorkbookTitle(archive) ?? spreadsheetId;

            var sheets = workbookDocument.Root?
                .Element(MainNs + "sheets")?
                .Elements(MainNs + "sheet")
                .Select((sheetElement, index) =>
                {
                    var relId = sheetElement.Attribute(RelationshipsNs + "id")?.Value ?? "";
                    var target = workbookRelationships.TryGetValue(relId, out var relTarget) ? relTarget : "";
                    if (!string.IsNullOrWhiteSpace(target) && !target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                    {
                        target = $"xl/{target.TrimStart('/')}";
                    }
                    return LoadSheet(
                        archive,
                        target,
                        sheetElement.Attribute("name")?.Value ?? $"Sheet{index + 1}",
                        index,
                        string.Equals(sheetElement.Attribute("state")?.Value, "hidden", StringComparison.OrdinalIgnoreCase),
                        sharedStrings,
                        styles);
                })
                .ToList() ?? new List<ParsedWorkbookSheet>();

            return new ParsedWorkbook
            {
                SpreadsheetId = spreadsheetId,
                SourceWorkbookUrl = sourceWorkbookUrl,
                Title = title,
                Sheets = sheets,
            };
        }

        private static Dictionary<string, string> LoadWorkbookRelationships(ZipArchive archive)
        {
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var document = XDocument.Load(relsEntry.Open());
            return document.Root?
                .Elements(PackageRelationshipsNs + "Relationship")
                .Where(x => x.Attribute("Id") is not null && x.Attribute("Target") is not null)
                .ToDictionary(x => x.Attribute("Id")!.Value, x => x.Attribute("Target")!.Value.Replace("\\", "/"), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> LoadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry is null) return new List<string>();

            var document = XDocument.Load(entry.Open());
            return document.Root?
                .Elements(MainNs + "si")
                .Select(ReadSharedString)
                .ToList() ?? new List<string>();
        }

        private static string? LoadWorkbookTitle(ZipArchive archive)
        {
            var entry = archive.GetEntry("docProps/core.xml");
            if (entry is null) return null;
            var document = XDocument.Load(entry.Open());
            return document.Root?.Element(DcNs + "title")?.Value?.Trim();
        }

        private static ParsedWorkbookSheet LoadSheet(
            ZipArchive archive,
            string path,
            string sheetName,
            int index,
            bool isHidden,
            List<string> sharedStrings,
            WorkbookStyleContext styles)
        {
            var entry = archive.GetEntry(path);
            if (entry is null)
            {
                return new ParsedWorkbookSheet { Name = sheetName, Index = index, IsHidden = isHidden };
            }

            var document = XDocument.Load(entry.Open());
            var sheet = new ParsedWorkbookSheet
            {
                Name = sheetName,
                Index = index,
                IsHidden = isHidden,
            };

            var cells = document.Root?
                .Element(MainNs + "sheetData")?
                .Elements(MainNs + "row")
                .SelectMany(row => row.Elements(MainNs + "c"))
                .Select(cellElement => ReadCell(cellElement, sharedStrings, styles))
                .ToList() ?? new List<ParsedWorkbookCell>();

            foreach (var cell in cells)
            {
                sheet.Cells[cell.Reference] = cell;
                sheet.CellsByIndex[(cell.Row, cell.Column)] = cell;
                if (cell.Row > sheet.MaxRow) sheet.MaxRow = cell.Row;
                if (cell.Column > sheet.MaxColumn) sheet.MaxColumn = cell.Column;
            }

            sheet.MergedRanges = document.Root?
                .Element(MainNs + "mergeCells")?
                .Elements(MainNs + "mergeCell")
                .Select(merge => ParseMergedRange(merge.Attribute("ref")?.Value ?? ""))
                .Where(x => x is not null)
                .Cast<ParsedWorkbookMergedRange>()
                .ToList() ?? new List<ParsedWorkbookMergedRange>();

            return sheet;
        }

        private static ParsedWorkbookCell ReadCell(XElement cellElement, List<string> sharedStrings, WorkbookStyleContext styles)
        {
            var reference = cellElement.Attribute("r")?.Value ?? "";
            ParseReference(reference, out var row, out var column);
            var cellType = cellElement.Attribute("t")?.Value ?? "";
            var styleIndex = int.TryParse(cellElement.Attribute("s")?.Value, out var parsedStyle) ? parsedStyle : -1;
            var rawValue = cellElement.Element(MainNs + "v")?.Value ?? "";
            var inlineText = cellElement.Element(MainNs + "is")?.Value ?? "";

            var value = cellType switch
            {
                "s" when int.TryParse(rawValue, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count => sharedStrings[sharedIndex],
                "inlineStr" => inlineText,
                "b" => rawValue == "1" ? "TRUE" : "FALSE",
                _ => styles.FormatCell(rawValue, styleIndex)
            };

            return new ParsedWorkbookCell
            {
                Reference = reference,
                Row = row,
                Column = column,
                Value = value,
                BackgroundColor = styles.GetBackgroundColor(styleIndex),
            };
        }

        private static ParsedWorkbookMergedRange? ParseMergedRange(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            var parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            ParseReference(parts[0], out var startRow, out var startColumn);
            ParseReference(parts.Length > 1 ? parts[1] : parts[0], out var endRow, out var endColumn);
            return new ParsedWorkbookMergedRange
            {
                Reference = reference,
                StartRow = startRow,
                EndRow = endRow,
                StartColumn = startColumn,
                EndColumn = endColumn,
            };
        }

        private static void ParseReference(string reference, out int row, out int column)
        {
            var match = Regex.Match(reference, @"^(?<col>[A-Z]+)(?<row>\d+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                row = 0;
                column = 0;
                return;
            }

            row = int.Parse(match.Groups["row"].Value, CultureInfo.InvariantCulture);
            column = 0;
            foreach (var ch in match.Groups["col"].Value.ToUpperInvariant())
            {
                column = (column * 26) + (ch - 'A' + 1);
            }
        }

        private static string ReadSharedString(XElement si)
        {
            var directText = si.Element(MainNs + "t")?.Value;
            if (!string.IsNullOrWhiteSpace(directText)) return directText;
            return string.Concat(si.Descendants(MainNs + "t").Select(x => x.Value));
        }
    }

    internal sealed class WorkbookStyleContext
    {
        private readonly List<CellStyleInfo> _styles;
        private readonly Dictionary<uint, string> _customFormats;

        private WorkbookStyleContext(List<CellStyleInfo> styles, Dictionary<uint, string> customFormats)
        {
            _styles = styles;
            _customFormats = customFormats;
        }

        public static WorkbookStyleContext Load(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/styles.xml");
            if (entry is null)
            {
                return new WorkbookStyleContext(new List<CellStyleInfo>(), new Dictionary<uint, string>());
            }

            var document = XDocument.Load(entry.Open());
            var customFormats = document.Root?
                .Element(WorkbookXlsxReader.MainNs + "numFmts")?
                .Elements(WorkbookXlsxReader.MainNs + "numFmt")
                .Where(x => x.Attribute("numFmtId") is not null && x.Attribute("formatCode") is not null)
                .ToDictionary(x => uint.Parse(x.Attribute("numFmtId")!.Value, CultureInfo.InvariantCulture), x => x.Attribute("formatCode")!.Value)
                ?? new Dictionary<uint, string>();

            var fills = document.Root?
                .Element(WorkbookXlsxReader.MainNs + "fills")?
                .Elements(WorkbookXlsxReader.MainNs + "fill")
                .Select(fill => fill.Descendants(WorkbookXlsxReader.MainNs + "fgColor").FirstOrDefault()?.Attribute("rgb")?.Value)
                .ToList() ?? new List<string?>();

            var styles = document.Root?
                .Element(WorkbookXlsxReader.MainNs + "cellXfs")?
                .Elements(WorkbookXlsxReader.MainNs + "xf")
                .Select(xf =>
                {
                    var numFmtId = uint.TryParse(xf.Attribute("numFmtId")?.Value, out var parsedNumFmt) ? parsedNumFmt : 0;
                    var fillId = int.TryParse(xf.Attribute("fillId")?.Value, out var parsedFill) ? parsedFill : -1;
                    var background = fillId >= 0 && fillId < fills.Count ? fills[fillId] : null;
                    return new CellStyleInfo(numFmtId, background);
                })
                .ToList() ?? new List<CellStyleInfo>();

            return new WorkbookStyleContext(styles, customFormats);
        }

        public string FormatCell(string rawValue, int styleIndex)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return "";
            if (!double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric)) return rawValue;

            var formatCode = GetFormatCode(styleIndex);
            if (string.IsNullOrWhiteSpace(formatCode)) return rawValue;

            var lower = formatCode.ToLowerInvariant();
            var isDate = lower.Contains("yy") || lower.Contains("dd") || lower.Contains("mm");
            var isTime = lower.Contains("h");
            if (!isDate && !isTime) return rawValue;

            var dateTime = DateTime.FromOADate(numeric);
            if (isDate && isTime) return dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (isDate) return dateTime.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
            return dateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public string? GetBackgroundColor(int styleIndex)
            => styleIndex >= 0 && styleIndex < _styles.Count ? _styles[styleIndex].BackgroundColor : null;

        private string? GetFormatCode(int styleIndex)
        {
            if (styleIndex < 0 || styleIndex >= _styles.Count) return null;
            var numFmtId = _styles[styleIndex].NumberFormatId;
            if (_customFormats.TryGetValue(numFmtId, out var custom)) return custom;
            return numFmtId switch
            {
                14 or 15 or 16 or 17 or 22 => "m/d/yyyy",
                18 or 19 or 20 or 21 => "h:mm",
                45 or 46 or 47 => "mm:ss",
                _ => null
            };
        }

        private sealed record CellStyleInfo(uint NumberFormatId, string? BackgroundColor);
    }

    private sealed record CanonicalFieldCatalog(
        List<CanonicalFieldOptionDto> Options,
        List<CanonicalFieldEntry> Entries,
        Dictionary<string, CanonicalFieldEntry> ByLookupKey);

    private readonly record struct TimeHeaderSlot(TimeOnly StartTime, TimeOnly? EndTime);

    private sealed record CanonicalFieldEntry(string FieldId, string CanonicalFieldName, string FieldName, string ParkName)
    {
        public IEnumerable<string> LookupKeys
            => new[]
            {
                NormalizeLookupKey(CanonicalFieldName),
                NormalizeLookupKey(FieldName),
                NormalizeLookupKey($"{ParkName}{FieldName}"),
                NormalizeLookupKey($"{ParkName}>{FieldName}")
            }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record FieldResolution(bool IsMapped, string? FieldId, string? FieldName, string Confidence);

    private sealed record InventoryCellStatus(
        bool HasMeaningfulInventory,
        string AvailabilityStatus,
        string UtilizationStatus,
        string? UsageType,
        string? UsedBy,
        string? AssignedGroup,
        string? AssignedDivision,
        string? AssignedTeamOrEvent,
        string Confidence,
        bool IsExternalUsage)
    {
        public static InventoryCellStatus None()
            => new(false, FieldInventoryAvailabilityStatuses.Unknown, FieldInventoryUtilizationStatuses.Unknown, null, null, null, null, null, FieldInventoryConfidence.Low, false);
    }
}
