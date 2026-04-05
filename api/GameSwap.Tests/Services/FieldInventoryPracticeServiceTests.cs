using System;
using System.Collections.Generic;
using System.IO;
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

public class FieldInventoryPracticeServiceTests
{
    private readonly InMemoryFieldInventoryImportRepository _inventoryRepository = new();
    private readonly InMemoryFieldRepository _fieldRepository = new();
    private readonly InMemoryMembershipRepository _membershipRepository = new();
    private readonly InMemoryDivisionRepository _divisionRepository = new();
    private readonly InMemoryTeamRepository _teamRepository = new();
    private readonly InMemorySlotRepository _slotRepository = new();
    private readonly InMemoryPracticeRequestRepository _practiceRequestRepository = new();
    private readonly Mock<ILogger<FieldInventoryImportService>> _logger = new();
    private readonly Mock<ILogger<PracticeRequestService>> _practiceRequestLogger = new();

    [Fact]
    public async Task RealAgsaWorkbook_ProducesAdminMappingsAndCommissionerReviewRequests()
    {
        SeedCanonicalFields();
        _divisionRepository.UpsertDivision("league-1", "TESTDIV", "Workbook Test Division");
        _teamRepository.UpsertTeam("league-1", "TESTDIV", "TEAM-TEST-1", "Workbook Test Team");
        var importService = CreateImportService();
        var practiceService = CreatePracticeService();
        var adminContext = CorrelationContext.Create("admin-1", "league-1");

        await ImportCurrentSeasonAsync(importService, adminContext);

        var initial = await practiceService.GetAdminViewAsync("Spring 2026", adminContext);

        Assert.True(initial.Summary.TotalRecords > 100, "Expected committed live inventory rows from the real AGSA workbook.");
        Assert.Contains(initial.Rows, row => row.MappingIssues.Count > 0);

        var syntheticRecord = new FieldInventoryLiveRecordEntity
        {
            Id = "live-test-review-1",
            LeagueId = "league-1",
            SeasonLabel = "Spring 2026",
            ImportRunId = "run-test-review",
            FieldId = "agsa/barcroft-3",
            FieldName = "AGSA > Barcroft #3",
            RawFieldName = "Barcroft #3",
            Date = "2026-03-18",
            DayOfWeek = "Wednesday",
            StartTime = "18:00",
            EndTime = "19:30",
            SlotDurationMinutes = 90,
            AvailabilityStatus = FieldInventoryAvailabilityStatuses.Available,
            UtilizationStatus = FieldInventoryUtilizationStatuses.NotUsed,
            UsedBy = "AGSA",
            AssignedGroup = "Workbook Review Group",
            AssignedDivision = "Workbook Review Division",
            SourceWorkbookUrl = "uploaded://real-workbook-fixture",
            SourceTab = "Synthetic",
            SourceCellRange = "Z99",
            SourceValue = "Workbook Review Group",
            ParserType = FieldInventoryParserTypes.SeasonWeekdayGrid,
            Confidence = FieldInventoryConfidence.High,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var liveRecords = await _inventoryRepository.GetLiveRecordsAsync("league-1", "Spring 2026");
        await _inventoryRepository.ReplaceLiveRecordsAsync("league-1", "Spring 2026", liveRecords.Concat(new[] { syntheticRecord }));

        await practiceService.SaveDivisionAliasAsync(
            new FieldInventoryDivisionAliasSaveRequest("Workbook Review Division", "TESTDIV"),
            "admin-1",
            adminContext);
        var admin = await practiceService.SaveGroupPolicyAsync(
            new FieldInventoryGroupPolicySaveRequest("Workbook Review Group", FieldInventoryPracticeBookingPolicies.CommissionerReview),
            "admin-1",
            adminContext);

        Assert.True(admin.Summary.RequestableBlocks > 0, "Expected at least one requestable 90-minute block after mapping the synthetic commissioner-review fixture.");
        Assert.True(admin.Summary.CommissionerReviewBlocks > 0, "Expected commissioner-review blocks after saving a commissioner-review policy.");
        Assert.Contains(admin.Rows, row =>
            string.Equals(row.RecordId, syntheticRecord.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.CanonicalDivisionCode, "TESTDIV", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.BookingPolicy, FieldInventoryPracticeBookingPolicies.CommissionerReview, StringComparison.OrdinalIgnoreCase));

        _membershipRepository.UpsertMembership(new TableEntity("coach-1", "league-1")
        {
            ["Role"] = Constants.Roles.Coach,
            ["Division"] = "TESTDIV",
            ["TeamId"] = "TEAM-TEST-1",
            ["TeamName"] = "Workbook Test Team",
        });

        var coachContext = CorrelationContext.Create("coach-1", "league-1");
        var coachView = await practiceService.GetCoachViewAsync("Spring 2026", "coach-1", coachContext);
        var commissionerReviewSlot = Assert.Single(coachView.Slots.Where(slot =>
            string.Equals(slot.LiveRecordId, syntheticRecord.Id, StringComparison.OrdinalIgnoreCase) &&
            slot.BookingPolicy == FieldInventoryPracticeBookingPolicies.CommissionerReview).Take(1));

        var afterCommissionerRequest = await practiceService.CreatePracticeRequestAsync(
            new FieldInventoryPracticeRequestCreateRequest("Spring 2026", commissionerReviewSlot.PracticeSlotKey, null, "Commissioner review validation"),
            "coach-1",
            coachContext);
        var pendingRequest = Assert.Single(afterCommissionerRequest.Requests.Where(r => r.PracticeSlotKey == commissionerReviewSlot.PracticeSlotKey));
        Assert.Equal(FieldInventoryPracticeRequestStatuses.Pending, pendingRequest.Status);

        var adminAfterRequests = await practiceService.GetAdminViewAsync("Spring 2026", adminContext);
        Assert.Contains(adminAfterRequests.Requests, r => r.PracticeSlotKey == commissionerReviewSlot.PracticeSlotKey && r.Status == FieldInventoryPracticeRequestStatuses.Pending);
    }

    [Fact]
    public async Task PonytailAssignedInventory_AutoApprovesCoachRequests()
    {
        SeedPonyPracticeReferenceData();
        await _inventoryRepository.ReplaceLiveRecordsAsync("league-1", "Spring 2026", new[]
        {
            new FieldInventoryLiveRecordEntity
            {
                Id = "live-ponytail-1",
                LeagueId = "league-1",
                SeasonLabel = "Spring 2026",
                ImportRunId = "run-1",
                FieldId = "agsa/barcroft-3",
                FieldName = "AGSA > Barcroft #3",
                RawFieldName = "Barcroft #3",
                Date = "2026-03-16",
                DayOfWeek = "Monday",
                StartTime = "18:00",
                EndTime = "19:30",
                SlotDurationMinutes = 90,
                AvailabilityStatus = FieldInventoryAvailabilityStatuses.Available,
                UtilizationStatus = FieldInventoryUtilizationStatuses.NotUsed,
                UsedBy = "AGSA",
                AssignedGroup = "Ponytail",
                AssignedDivision = "PY Practice",
                SourceWorkbookUrl = "uploaded://fixture",
                SourceTab = "Spring 316-522",
                SourceCellRange = "B20",
                SourceValue = "PY Practice",
                ParserType = FieldInventoryParserTypes.SeasonWeekdayGrid,
                Confidence = FieldInventoryConfidence.High,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        });
        await _inventoryRepository.AddCommitRunAsync(new FieldInventoryCommitRunEntity
        {
            Id = "commit-1",
            LeagueId = "league-1",
            ImportRunId = "run-1",
            SeasonLabel = "Spring 2026",
            Mode = FieldInventoryCommitModes.Upsert,
            DryRun = false,
            CreateCount = 1,
            UpdateCount = 0,
            DeleteCount = 0,
            UnchangedCount = 0,
            SkippedUnmappedCount = 0,
            CreatedBy = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _membershipRepository.UpsertMembership(new TableEntity("coach-1", "league-1")
        {
            ["Role"] = Constants.Roles.Coach,
            ["Division"] = "PONY",
            ["TeamId"] = "TEAM-PONY-1",
            ["TeamName"] = "Ponytails Red",
        });

        var practiceService = CreatePracticeService();
        var coachContext = CorrelationContext.Create("coach-1", "league-1");
        var coachView = await practiceService.GetCoachViewAsync("Spring 2026", "coach-1", coachContext);
        var slot = Assert.Single(coachView.Slots);
        Assert.Equal(FieldInventoryPracticeBookingPolicies.AutoApprove, slot.BookingPolicy);

        var afterRequest = await practiceService.CreatePracticeRequestAsync(
            new FieldInventoryPracticeRequestCreateRequest("Spring 2026", slot.PracticeSlotKey, null, "Auto approve"),
            "coach-1",
            coachContext);

        var request = Assert.Single(afterRequest.Requests);
        Assert.Equal(FieldInventoryPracticeRequestStatuses.Approved, request.Status);
    }

    [Fact]
    public async Task RealAgsaWorkbook_AdminMappingsCanResolvePolicyDivisionAndTeam()
    {
        SeedCanonicalFields();
        var importService = CreateImportService();
        var practiceService = CreatePracticeService();
        var adminContext = CorrelationContext.Create("admin-1", "league-1");

        await ImportCurrentSeasonAsync(importService, adminContext);

        var initial = await practiceService.GetAdminViewAsync("Spring 2026", adminContext);
        var policyRow = initial.Rows.FirstOrDefault(row =>
            row.BookingPolicy == FieldInventoryPracticeBookingPolicies.NotRequestable &&
            row.MappingIssues.Contains("policy_unmapped") &&
            !string.IsNullOrWhiteSpace(row.AssignedGroup));
        Assert.NotNull(policyRow);

        var mappedDivisionRow = initial.Rows.FirstOrDefault(row =>
            row.MappingIssues.Contains("division_unmapped") &&
            !string.IsNullOrWhiteSpace(row.RawAssignedDivision));
        Assert.NotNull(mappedDivisionRow);

        var mappedTeamRow = initial.Rows.FirstOrDefault(row =>
            row.MappingIssues.Contains("team_unmapped") &&
            !string.IsNullOrWhiteSpace(row.RawAssignedTeamOrEvent));
        Assert.NotNull(mappedTeamRow);

        _divisionRepository.UpsertDivision("league-1", "PONY", "Ponytail");
        var afterDivision = await practiceService.SaveDivisionAliasAsync(
            new FieldInventoryDivisionAliasSaveRequest(mappedDivisionRow!.RawAssignedDivision, "PONY"),
            "admin-1",
            adminContext);
        Assert.Contains(afterDivision.Rows, row =>
            string.Equals(row.RawAssignedDivision, mappedDivisionRow.RawAssignedDivision, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.CanonicalDivisionCode, "PONY", StringComparison.OrdinalIgnoreCase));

        var mappedTeamAfterDivisionRow = afterDivision.Rows.FirstOrDefault(row =>
            string.Equals(row.CanonicalDivisionCode, "PONY", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(row.RawAssignedTeamOrEvent));
        Assert.NotNull(mappedTeamAfterDivisionRow);

        _teamRepository.UpsertTeam("league-1", "PONY", "TEAM-PONY-1", "Ponytails Red");
        var afterTeam = await practiceService.SaveTeamAliasAsync(
            new FieldInventoryTeamAliasSaveRequest(mappedTeamAfterDivisionRow!.RawAssignedTeamOrEvent, "PONY", "TEAM-PONY-1", "Ponytails Red"),
            "admin-1",
            adminContext);
        Assert.Contains(afterTeam.Rows, row =>
            string.Equals(row.RawAssignedTeamOrEvent, mappedTeamAfterDivisionRow.RawAssignedTeamOrEvent, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.CanonicalTeamId, "TEAM-PONY-1", StringComparison.OrdinalIgnoreCase));

        var afterPolicy = await practiceService.SaveGroupPolicyAsync(
            new FieldInventoryGroupPolicySaveRequest(policyRow!.AssignedGroup, FieldInventoryPracticeBookingPolicies.CommissionerReview),
            "admin-1",
            adminContext);
        Assert.Contains(afterPolicy.Rows, row =>
            string.Equals(row.AssignedGroup, policyRow.AssignedGroup, StringComparison.OrdinalIgnoreCase) &&
            row.BookingPolicy == FieldInventoryPracticeBookingPolicies.CommissionerReview);
    }

    private async Task ImportCurrentSeasonAsync(FieldInventoryImportService importService, CorrelationContext context)
    {
        var inspect = await importService.InspectUploadedWorkbookAsync(
            "2026 AGSA Spring Field Grid (1).xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            File.ReadAllBytes(GetRealAgsaWorkbookPath()),
            context);

        var preview = await importService.CreatePreviewAsync(new FieldInventoryPreviewRequest(
            null,
            inspect.UploadedWorkbookId,
            "Spring 2026",
            null,
            new List<FieldInventorySelectedTab>
            {
                new("Spring 316-522", FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, true),
                new("Spring 525-619", FieldInventoryParserTypes.SeasonWeekdayGrid, FieldInventoryActionTypes.Ingest, true),
                new("Weekends", FieldInventoryParserTypes.WeekendGrid, FieldInventoryActionTypes.Ingest, true),
            }), context);

        Assert.True(preview.Run.SummaryCounts.ParsedRecords > 100, "Expected the current-season workbook preview to parse substantial inventory.");
        await importService.CommitRunAsync(
            preview.Run.Id,
            new FieldInventoryCommitRequest(FieldInventoryCommitModes.Upsert, false, true),
            context);
    }

    private void SeedCanonicalFields()
    {
        _fieldRepository.AddField("league-1", "agsa", "alcova-heights", "AGSA", "Alcova Heights", "AGSA > Alcova Heights");
        _fieldRepository.AddField("league-1", "agsa", "barcroft-3", "AGSA", "Barcroft #3", "AGSA > Barcroft #3");
        _fieldRepository.AddField("league-1", "agsa", "key-1", "AGSA", "Key #1", "AGSA > Key #1");
        _fieldRepository.AddField("league-1", "agsa", "key-2", "AGSA", "Key #2", "AGSA > Key #2");
        _fieldRepository.AddField("league-1", "agsa", "ats-1", "AGSA", "Key (former ATS) 1", "AGSA > Key (former ATS) 1");
        _fieldRepository.AddField("league-1", "agsa", "ats-2", "AGSA", "Key (former ATS) 2", "AGSA > Key (former ATS) 2");
        _fieldRepository.AddField("league-1", "agsa", "gb1", "AGSA", "Greenbrier #1", "AGSA > Greenbrier #1");
        _fieldRepository.AddField("league-1", "agsa", "gb2", "AGSA", "Greenbrier #2", "AGSA > Greenbrier #2");
    }

    private void SeedPonyPracticeReferenceData()
    {
        _divisionRepository.UpsertDivision("league-1", "PONY", "PY Practice");
        _teamRepository.UpsertTeam("league-1", "PONY", "TEAM-PONY-1", "Ponytails Red");
    }

    private FieldInventoryImportService CreateImportService()
        => new(_inventoryRepository, _fieldRepository, new ThrowingWorkbookConnector(), _logger.Object);

    private FieldInventoryPracticeService CreatePracticeService()
        => new(
            _inventoryRepository,
            _membershipRepository,
            _divisionRepository,
            _teamRepository,
            _slotRepository,
            _practiceRequestRepository,
            new PracticeRequestService(
                _practiceRequestRepository,
                _membershipRepository,
                _slotRepository,
                _teamRepository,
                _practiceRequestLogger.Object));

    private static string GetRealAgsaWorkbookPath()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "2026 AGSA Spring Field Grid (1).xlsx"));

    private sealed class ThrowingWorkbookConnector : FieldInventoryImportService.IFieldInventoryWorkbookConnector
    {
        public Task<ParsedWorkbook> LoadWorkbookAsync(string sourceWorkbookUrl)
            => throw new InvalidOperationException($"Unexpected URL workbook load for {sourceWorkbookUrl}.");
    }

    private sealed class InMemoryFieldInventoryImportRepository : IFieldInventoryImportRepository
    {
        private readonly Dictionary<string, FieldInventoryImportRunEntity> _runs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryStagedRecordEntity>> _records = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryWarningEntity>> _warnings = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryReviewQueueItemEntity>> _reviewItems = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryDiagnosticEntity>> _diagnostics = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryFieldAliasEntity>> _aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryTabClassificationEntity>> _tabClassifications = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryLiveRecordEntity>> _liveRecords = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryCommitRunEntity>> _commitRuns = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryDivisionAliasEntity>> _divisionAliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryTeamAliasEntity>> _teamAliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FieldInventoryGroupPolicyEntity>> _groupPolicies = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FieldInventoryWorkbookUploadEntity> _uploads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _uploadBytes = new(StringComparer.OrdinalIgnoreCase);

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

        public Task AddDiagnosticAsync(FieldInventoryDiagnosticEntity diagnostic)
        {
            var key = $"{diagnostic.LeagueId}|{diagnostic.ClientRequestId}";
            if (!_diagnostics.ContainsKey(key))
            {
                _diagnostics[key] = new List<FieldInventoryDiagnosticEntity>();
            }

            _diagnostics[key].Add(diagnostic);
            return Task.CompletedTask;
        }

        public Task<List<FieldInventoryDiagnosticEntity>> GetDiagnosticsAsync(string leagueId, string clientRequestId)
        {
            _diagnostics.TryGetValue($"{leagueId}|{clientRequestId}", out var list);
            return Task.FromResult(list?.ToList() ?? new List<FieldInventoryDiagnosticEntity>());
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

        public Task SaveWorkbookUploadAsync(FieldInventoryWorkbookUploadEntity upload, byte[] workbookBytes)
        {
            _uploads[$"{upload.LeagueId}|{upload.Id}"] = upload;
            _uploadBytes[$"{upload.LeagueId}|{upload.Id}"] = workbookBytes.ToArray();
            return Task.CompletedTask;
        }

        public Task<FieldInventoryWorkbookUploadEntity?> GetWorkbookUploadAsync(string leagueId, string uploadId)
        {
            _uploads.TryGetValue($"{leagueId}|{uploadId}", out var upload);
            return Task.FromResult(upload);
        }

        public Task<byte[]?> GetWorkbookUploadBytesAsync(string leagueId, string uploadId)
        {
            _uploadBytes.TryGetValue($"{leagueId}|{uploadId}", out var bytes);
            return Task.FromResult(bytes);
        }

        public Task<List<FieldInventoryLiveRecordEntity>> GetLiveRecordsAsync(string leagueId, string seasonLabel)
            => Task.FromResult(_liveRecords.TryGetValue($"{leagueId}|{seasonLabel}", out var list) ? list.ToList() : new List<FieldInventoryLiveRecordEntity>());

        public Task ReplaceLiveRecordsAsync(string leagueId, string seasonLabel, IEnumerable<FieldInventoryLiveRecordEntity> records)
        {
            _liveRecords[$"{leagueId}|{seasonLabel}"] = records.ToList();
            return Task.CompletedTask;
        }

        public Task AddCommitRunAsync(FieldInventoryCommitRunEntity commitRun)
        {
            if (!_commitRuns.ContainsKey(commitRun.LeagueId))
            {
                _commitRuns[commitRun.LeagueId] = new List<FieldInventoryCommitRunEntity>();
            }

            _commitRuns[commitRun.LeagueId].Add(commitRun);
            return Task.CompletedTask;
        }

        public Task<List<FieldInventoryCommitRunEntity>> GetCommitRunsAsync(string leagueId)
            => Task.FromResult(_commitRuns.TryGetValue(leagueId, out var list) ? list.OrderByDescending(x => x.CreatedAt).ToList() : new List<FieldInventoryCommitRunEntity>());

        public Task<List<FieldInventoryDivisionAliasEntity>> GetDivisionAliasesAsync(string leagueId)
            => Task.FromResult(_divisionAliases.TryGetValue(leagueId, out var list) ? list.ToList() : new List<FieldInventoryDivisionAliasEntity>());

        public Task UpsertDivisionAliasAsync(FieldInventoryDivisionAliasEntity alias)
        {
            if (!_divisionAliases.ContainsKey(alias.LeagueId))
            {
                _divisionAliases[alias.LeagueId] = new List<FieldInventoryDivisionAliasEntity>();
            }

            _divisionAliases[alias.LeagueId].RemoveAll(x => x.NormalizedLookupKey == alias.NormalizedLookupKey);
            _divisionAliases[alias.LeagueId].Add(alias);
            return Task.CompletedTask;
        }

        public Task<List<FieldInventoryTeamAliasEntity>> GetTeamAliasesAsync(string leagueId)
            => Task.FromResult(_teamAliases.TryGetValue(leagueId, out var list) ? list.ToList() : new List<FieldInventoryTeamAliasEntity>());

        public Task UpsertTeamAliasAsync(FieldInventoryTeamAliasEntity alias)
        {
            if (!_teamAliases.ContainsKey(alias.LeagueId))
            {
                _teamAliases[alias.LeagueId] = new List<FieldInventoryTeamAliasEntity>();
            }

            _teamAliases[alias.LeagueId].RemoveAll(x => x.Id == alias.Id);
            _teamAliases[alias.LeagueId].Add(alias);
            return Task.CompletedTask;
        }

        public Task<List<FieldInventoryGroupPolicyEntity>> GetGroupPoliciesAsync(string leagueId)
            => Task.FromResult(_groupPolicies.TryGetValue(leagueId, out var list) ? list.ToList() : new List<FieldInventoryGroupPolicyEntity>());

        public Task UpsertGroupPolicyAsync(FieldInventoryGroupPolicyEntity policy)
        {
            if (!_groupPolicies.ContainsKey(policy.LeagueId))
            {
                _groupPolicies[policy.LeagueId] = new List<FieldInventoryGroupPolicyEntity>();
            }

            _groupPolicies[policy.LeagueId].RemoveAll(x => x.NormalizedLookupKey == policy.NormalizedLookupKey);
            _groupPolicies[policy.LeagueId].Add(policy);
            return Task.CompletedTask;
        }

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
                ["IsActive"] = true,
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

    private sealed class InMemoryMembershipRepository : IMembershipRepository
    {
        private readonly Dictionary<string, TableEntity> _memberships = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _globalAdmins = new(StringComparer.OrdinalIgnoreCase);

        public void UpsertMembership(TableEntity membership)
        {
            _memberships[$"{membership.PartitionKey}|{membership.RowKey}"] = membership;
        }

        public Task<TableEntity?> GetMembershipAsync(string userId, string leagueId)
        {
            _memberships.TryGetValue($"{userId}|{leagueId}", out var membership);
            return Task.FromResult(membership);
        }

        public Task<bool> IsGlobalAdminAsync(string userId) => Task.FromResult(_globalAdmins.Contains(userId));
        public Task<bool> IsMemberAsync(string userId, string leagueId) => Task.FromResult(_memberships.ContainsKey($"{userId}|{leagueId}"));
        public Task<List<TableEntity>> GetUserMembershipsAsync(string userId) => Task.FromResult(_memberships.Values.Where(x => x.PartitionKey == userId).ToList());

        public Task<PaginationResult<TableEntity>> QueryLeagueMembershipsAsync(string leagueId, string? role = null, string? continuationToken = null, int pageSize = 50)
            => Task.FromResult(new PaginationResult<TableEntity>
            {
                Items = _memberships.Values.Where(x => x.RowKey == leagueId).ToList(),
                ContinuationToken = null,
                PageSize = pageSize,
            });

        public Task CreateMembershipAsync(TableEntity membership)
        {
            UpsertMembership(membership);
            return Task.CompletedTask;
        }

        public Task UpdateMembershipAsync(TableEntity membership)
        {
            UpsertMembership(membership);
            return Task.CompletedTask;
        }

        public Task DeleteMembershipAsync(string userId, string leagueId)
        {
            _memberships.Remove($"{userId}|{leagueId}");
            return Task.CompletedTask;
        }

        public Task UpsertMembershipAsync(TableEntity membership)
        {
            UpsertMembership(membership);
            return Task.CompletedTask;
        }

        public Task<List<TableEntity>> QueryAllMembershipsAsync(string? leagueFilter = null)
        {
            var items = string.IsNullOrWhiteSpace(leagueFilter)
                ? _memberships.Values.ToList()
                : _memberships.Values.Where(x => x.RowKey == leagueFilter).ToList();
            return Task.FromResult(items);
        }

        public Task<List<TableEntity>> GetLeagueMembershipsAsync(string leagueId)
            => Task.FromResult(_memberships.Values.Where(x => x.RowKey == leagueId).ToList());
    }

    private sealed class InMemoryDivisionRepository : IDivisionRepository
    {
        private readonly Dictionary<string, TableEntity> _divisions = new(StringComparer.OrdinalIgnoreCase);

        public void UpsertDivision(string leagueId, string code, string name)
        {
            _divisions[$"{leagueId}|{code}"] = new TableEntity($"DIV|{leagueId}", code)
            {
                ["Code"] = code,
                ["Name"] = name,
                ["IsActive"] = true,
            };
        }

        public Task<TableEntity?> GetDivisionAsync(string leagueId, string code)
        {
            _divisions.TryGetValue($"{leagueId}|{code}", out var division);
            return Task.FromResult(division);
        }

        public Task<List<TableEntity>> QueryDivisionsAsync(string leagueId)
            => Task.FromResult(_divisions.Where(x => x.Key.StartsWith($"{leagueId}|", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).ToList());

        public Task CreateDivisionAsync(TableEntity division)
        {
            _divisions[$"{division.PartitionKey}|{division.RowKey}"] = division;
            return Task.CompletedTask;
        }

        public Task UpdateDivisionAsync(TableEntity division)
        {
            _divisions[$"{division.PartitionKey}|{division.RowKey}"] = division;
            return Task.CompletedTask;
        }

        public Task<TableEntity?> GetTemplatesAsync(string leagueId) => Task.FromResult<TableEntity?>(null);
        public Task UpsertTemplatesAsync(TableEntity templates) => Task.CompletedTask;
    }

    private sealed class InMemoryTeamRepository : ITeamRepository
    {
        private readonly Dictionary<string, TableEntity> _teams = new(StringComparer.OrdinalIgnoreCase);

        public void UpsertTeam(string leagueId, string division, string teamId, string teamName)
        {
            _teams[$"{leagueId}|{division}|{teamId}"] = new TableEntity($"TEAM|{leagueId}|{division}", teamId)
            {
                ["Division"] = division,
                ["Name"] = teamName,
            };
        }

        public Task<TableEntity?> GetTeamAsync(string leagueId, string division, string teamId)
        {
            _teams.TryGetValue($"{leagueId}|{division}|{teamId}", out var team);
            return Task.FromResult(team);
        }

        public Task<List<TableEntity>> QueryTeamsByDivisionAsync(string leagueId, string division)
            => Task.FromResult(_teams.Where(x => x.Key.StartsWith($"{leagueId}|{division}|", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).ToList());

        public Task<List<TableEntity>> QueryAllTeamsAsync(string leagueId)
            => Task.FromResult(_teams.Where(x => x.Key.StartsWith($"{leagueId}|", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).ToList());

        public Task CreateTeamAsync(TableEntity team)
        {
            _teams[$"{team.PartitionKey}|{team.RowKey}"] = team;
            return Task.CompletedTask;
        }

        public Task UpdateTeamAsync(TableEntity team)
        {
            _teams[$"{team.PartitionKey}|{team.RowKey}"] = team;
            return Task.CompletedTask;
        }

        public Task DeleteTeamAsync(string leagueId, string division, string teamId)
        {
            _teams.Remove($"{leagueId}|{division}|{teamId}");
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySlotRepository : ISlotRepository
    {
        private readonly Dictionary<string, TableEntity> _slots = new(StringComparer.OrdinalIgnoreCase);

        public Task<TableEntity?> GetSlotAsync(string leagueId, string division, string slotId)
        {
            _slots.TryGetValue(BuildKey(leagueId, division, slotId), out var slot);
            return Task.FromResult(Clone(slot));
        }

        public Task<PaginationResult<TableEntity>> QuerySlotsAsync(SlotQueryFilter filter, string? continuationToken = null)
        {
            var items = _slots.Values
                .Where(slot => string.Equals(GetLeagueId(slot.PartitionKey), filter.LeagueId, StringComparison.OrdinalIgnoreCase))
                .Where(slot => string.IsNullOrWhiteSpace(filter.Division) || string.Equals(GetDivision(slot.PartitionKey), filter.Division, StringComparison.OrdinalIgnoreCase))
                .Where(slot => !filter.ExcludeCancelled || !string.Equals(slot.GetString("Status"), Constants.Status.SlotCancelled, StringComparison.OrdinalIgnoreCase))
                .Where(slot => !filter.ExcludeAvailability || slot.GetBoolean("IsAvailability") != true)
                .Where(slot => string.IsNullOrWhiteSpace(filter.FieldKey) || string.Equals(slot.GetString("FieldKey"), filter.FieldKey, StringComparison.OrdinalIgnoreCase))
                .Where(slot => string.IsNullOrWhiteSpace(filter.FromDate) || string.CompareOrdinal(slot.GetString("GameDate") ?? "", filter.FromDate) >= 0)
                .Where(slot => string.IsNullOrWhiteSpace(filter.ToDate) || string.CompareOrdinal(slot.GetString("GameDate") ?? "", filter.ToDate) <= 0)
                .Where(slot =>
                {
                    var status = slot.GetString("Status") ?? "";
                    if (filter.Statuses.Count > 0)
                    {
                        return filter.Statuses.Contains(status, StringComparer.OrdinalIgnoreCase);
                    }

                    return string.IsNullOrWhiteSpace(filter.Status) || string.Equals(status, filter.Status, StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(slot => slot.GetString("GameDate"))
                .ThenBy(slot => slot.GetString("StartTime"))
                .Select(Clone)
                .ToList();

            return Task.FromResult(new PaginationResult<TableEntity>
            {
                Items = items,
                ContinuationToken = null,
                PageSize = filter.PageSize,
            });
        }

        public Task<bool> HasConflictAsync(string leagueId, string fieldKey, string gameDate, int startMin, int endMin, string? excludeSlotId = null)
            => Task.FromResult(false);

        public Task CreateSlotAsync(TableEntity slot)
        {
            var clone = Clone(slot) ?? new TableEntity(slot.PartitionKey, slot.RowKey);
            clone.ETag = new Azure.ETag(Guid.NewGuid().ToString("N"));
            _slots[BuildKeyFromEntity(clone)] = clone;
            return Task.CompletedTask;
        }

        public Task UpdateSlotAsync(TableEntity slot, Azure.ETag etag)
        {
            var clone = Clone(slot) ?? new TableEntity(slot.PartitionKey, slot.RowKey);
            clone.ETag = new Azure.ETag(Guid.NewGuid().ToString("N"));
            _slots[BuildKeyFromEntity(clone)] = clone;
            return Task.CompletedTask;
        }

        public Task DeleteSlotAsync(string leagueId, string division, string slotId)
        {
            _slots.Remove(BuildKey(leagueId, division, slotId));
            return Task.CompletedTask;
        }

        public async Task CancelSlotAsync(string leagueId, string division, string slotId)
        {
            var slot = await GetSlotAsync(leagueId, division, slotId);
            if (slot is null) return;
            slot["Status"] = Constants.Status.SlotCancelled;
            await UpdateSlotAsync(slot, slot.ETag);
        }

        public Task<List<TableEntity>> GetSlotsByFieldAndDateAsync(string leagueId, string fieldKey, string gameDate)
            => Task.FromResult(
                _slots.Values
                    .Where(slot => string.Equals(GetLeagueId(slot.PartitionKey), leagueId, StringComparison.OrdinalIgnoreCase))
                    .Where(slot => string.Equals(slot.GetString("FieldKey"), fieldKey, StringComparison.OrdinalIgnoreCase))
                    .Where(slot => string.Equals(slot.GetString("GameDate"), gameDate, StringComparison.OrdinalIgnoreCase))
                    .Select(Clone)
                    .ToList());

        private static string BuildKey(string leagueId, string division, string slotId)
            => $"{leagueId}|{division}|{slotId}";

        private static string BuildKeyFromEntity(TableEntity slot)
            => BuildKey(GetLeagueId(slot.PartitionKey), GetDivision(slot.PartitionKey), slot.RowKey);

        private static string GetLeagueId(string partitionKey)
            => (partitionKey ?? "").Split('|').Skip(1).FirstOrDefault() ?? "";

        private static string GetDivision(string partitionKey)
            => (partitionKey ?? "").Split('|').Skip(2).FirstOrDefault() ?? "";

        private static TableEntity? Clone(TableEntity? source)
        {
            if (source is null) return null;
            var clone = new TableEntity(source.PartitionKey, source.RowKey)
            {
                Timestamp = source.Timestamp,
                ETag = source.ETag,
            };
            foreach (var pair in source)
            {
                clone[pair.Key] = pair.Value;
            }

            return clone;
        }
    }

    private sealed class InMemoryPracticeRequestRepository : IPracticeRequestRepository
    {
        private readonly Dictionary<string, TableEntity> _requests = new(StringComparer.OrdinalIgnoreCase);

        public Task CreateRequestAsync(TableEntity request)
        {
            var clone = Clone(request) ?? new TableEntity(request.PartitionKey, request.RowKey);
            clone.ETag = new Azure.ETag(Guid.NewGuid().ToString("N"));
            _requests[BuildKey(clone.GetString("LeagueId") ?? "", clone.RowKey)] = clone;
            return Task.CompletedTask;
        }

        public Task<TableEntity?> GetRequestAsync(string leagueId, string requestId)
        {
            _requests.TryGetValue(BuildKey(leagueId, requestId), out var request);
            return Task.FromResult(Clone(request));
        }

        public Task UpdateRequestAsync(TableEntity request, Azure.ETag etag)
        {
            var clone = Clone(request) ?? new TableEntity(request.PartitionKey, request.RowKey);
            clone.ETag = new Azure.ETag(Guid.NewGuid().ToString("N"));
            _requests[BuildKey(clone.GetString("LeagueId") ?? "", clone.RowKey)] = clone;
            return Task.CompletedTask;
        }

        public Task<List<TableEntity>> QueryRequestsAsync(string leagueId, string? status = null, string? division = null, string? teamId = null, string? slotId = null)
            => Task.FromResult(
                _requests.Values
                    .Where(request => string.Equals(request.GetString("LeagueId"), leagueId, StringComparison.OrdinalIgnoreCase))
                    .Where(request => string.IsNullOrWhiteSpace(status) || string.Equals(request.GetString("Status"), status, StringComparison.OrdinalIgnoreCase))
                    .Where(request => string.IsNullOrWhiteSpace(division) || string.Equals(request.GetString("Division"), division, StringComparison.OrdinalIgnoreCase))
                    .Where(request => string.IsNullOrWhiteSpace(teamId) || string.Equals(request.GetString("TeamId"), teamId, StringComparison.OrdinalIgnoreCase))
                    .Where(request => string.IsNullOrWhiteSpace(slotId) || string.Equals(request.GetString("SlotId"), slotId, StringComparison.OrdinalIgnoreCase))
                    .Select(Clone)
                    .ToList()!);

        public Task<int> CountRequestsForTeamAsync(string leagueId, string division, string teamId, IReadOnlyCollection<string> statuses)
            => Task.FromResult(QueryFiltered(leagueId, division, teamId, null, statuses).Count);

        public Task<bool> ExistsRequestForTeamSlotAsync(string leagueId, string division, string teamId, string slotId, IReadOnlyCollection<string> statuses)
            => Task.FromResult(QueryFiltered(leagueId, division, teamId, slotId, statuses).Any());

        public Task<List<TableEntity>> QuerySlotRequestsAsync(string leagueId, string division, string slotId, IReadOnlyCollection<string>? statuses = null)
            => Task.FromResult(QueryFiltered(leagueId, division, null, slotId, statuses).Select(Clone).ToList()!);

        private List<TableEntity> QueryFiltered(string leagueId, string division, string? teamId, string? slotId, IReadOnlyCollection<string>? statuses)
        {
            return _requests.Values
                .Where(request => string.Equals(request.GetString("LeagueId"), leagueId, StringComparison.OrdinalIgnoreCase))
                .Where(request => string.Equals(request.GetString("Division"), division, StringComparison.OrdinalIgnoreCase))
                .Where(request => string.IsNullOrWhiteSpace(teamId) || string.Equals(request.GetString("TeamId"), teamId, StringComparison.OrdinalIgnoreCase))
                .Where(request => string.IsNullOrWhiteSpace(slotId) || string.Equals(request.GetString("SlotId"), slotId, StringComparison.OrdinalIgnoreCase))
                .Where(request => statuses is null || statuses.Count == 0 || statuses.Contains(request.GetString("Status") ?? "", StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        private static string BuildKey(string leagueId, string requestId)
            => $"{leagueId}|{requestId}";

        private static TableEntity? Clone(TableEntity? source)
        {
            if (source is null) return null;
            var clone = new TableEntity(source.PartitionKey, source.RowKey)
            {
                Timestamp = source.Timestamp,
                ETag = source.ETag,
            };
            foreach (var pair in source)
            {
                clone[pair.Key] = pair.Value;
            }

            return clone;
        }
    }
}
