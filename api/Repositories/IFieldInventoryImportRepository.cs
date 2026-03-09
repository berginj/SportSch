using GameSwap.Functions.Models;

namespace GameSwap.Functions.Repositories;

public interface IFieldInventoryImportRepository
{
    Task UpsertImportRunAsync(FieldInventoryImportRunEntity run);
    Task<FieldInventoryImportRunEntity?> GetImportRunAsync(string leagueId, string runId);

    Task ReplaceRunDataAsync(
        string importRunId,
        IEnumerable<FieldInventoryStagedRecordEntity> records,
        IEnumerable<FieldInventoryWarningEntity> warnings,
        IEnumerable<FieldInventoryReviewQueueItemEntity> reviewItems);

    Task<List<FieldInventoryStagedRecordEntity>> GetStagedRecordsAsync(string importRunId);
    Task<List<FieldInventoryWarningEntity>> GetWarningsAsync(string importRunId);
    Task<List<FieldInventoryReviewQueueItemEntity>> GetReviewItemsAsync(string importRunId);
    Task UpsertReviewItemAsync(FieldInventoryReviewQueueItemEntity reviewItem);
    Task AddDiagnosticAsync(FieldInventoryDiagnosticEntity diagnostic);
    Task<List<FieldInventoryDiagnosticEntity>> GetDiagnosticsAsync(string leagueId, string clientRequestId);

    Task<List<FieldInventoryFieldAliasEntity>> GetFieldAliasesAsync(string leagueId);
    Task UpsertFieldAliasAsync(FieldInventoryFieldAliasEntity alias);

    Task<List<FieldInventoryTabClassificationEntity>> GetTabClassificationsAsync(string leagueId);
    Task UpsertTabClassificationAsync(FieldInventoryTabClassificationEntity classification);

    Task SaveWorkbookUploadAsync(FieldInventoryWorkbookUploadEntity upload, byte[] workbookBytes);
    Task<FieldInventoryWorkbookUploadEntity?> GetWorkbookUploadAsync(string leagueId, string uploadId);
    Task<byte[]?> GetWorkbookUploadBytesAsync(string leagueId, string uploadId);

    Task<List<FieldInventoryLiveRecordEntity>> GetLiveRecordsAsync(string leagueId, string seasonLabel);
    Task ReplaceLiveRecordsAsync(string leagueId, string seasonLabel, IEnumerable<FieldInventoryLiveRecordEntity> records);

    Task AddCommitRunAsync(FieldInventoryCommitRunEntity commitRun);
    Task<List<FieldInventoryCommitRunEntity>> GetCommitRunsAsync(string leagueId);

    Task<List<FieldInventoryDivisionAliasEntity>> GetDivisionAliasesAsync(string leagueId);
    Task UpsertDivisionAliasAsync(FieldInventoryDivisionAliasEntity alias);

    Task<List<FieldInventoryTeamAliasEntity>> GetTeamAliasesAsync(string leagueId);
    Task UpsertTeamAliasAsync(FieldInventoryTeamAliasEntity alias);

    Task<List<FieldInventoryGroupPolicyEntity>> GetGroupPoliciesAsync(string leagueId);
    Task UpsertGroupPolicyAsync(FieldInventoryGroupPolicyEntity policy);

    Task<List<FieldInventoryPracticeRequestEntity>> GetPracticeRequestsAsync(string leagueId, string seasonLabel);
    Task<FieldInventoryPracticeRequestEntity?> GetPracticeRequestAsync(string leagueId, string seasonLabel, string requestId);
    Task UpsertPracticeRequestAsync(FieldInventoryPracticeRequestEntity request);
}
