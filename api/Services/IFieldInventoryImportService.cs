using GameSwap.Functions.Models;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

public interface IFieldInventoryImportService
{
    Task<FieldInventoryWorkbookInspectResponse> InspectWorkbookAsync(string sourceWorkbookUrl, CorrelationContext context);
    Task<FieldInventoryPreviewResponse> CreatePreviewAsync(FieldInventoryPreviewRequest request, CorrelationContext context);
    Task<FieldInventoryPreviewResponse?> GetRunAsync(string runId, CorrelationContext context);
    Task<FieldInventoryPreviewResponse> StageRunAsync(string runId, CorrelationContext context);
    Task<FieldInventoryPreviewResponse> SaveFieldAliasAsync(FieldInventoryAliasSaveRequest request, CorrelationContext context);
    Task<FieldInventoryPreviewResponse> SaveTabClassificationAsync(FieldInventoryTabClassificationSaveRequest request, CorrelationContext context);
    Task<FieldInventoryPreviewResponse> UpdateReviewItemAsync(string runId, string reviewItemId, FieldInventoryReviewDecisionRequest request, CorrelationContext context);
    Task<FieldInventoryPreviewResponse> CommitRunAsync(string runId, FieldInventoryCommitRequest request, CorrelationContext context);
}
