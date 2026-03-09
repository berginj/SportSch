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
}
