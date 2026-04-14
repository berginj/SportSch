using GameSwap.Functions.Models;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

public interface IFieldInventoryPracticeService
{
    Task<FieldInventoryPracticeAdminResponse> GetAdminViewAsync(string? seasonLabel, CorrelationContext context);
    Task<FieldInventoryPracticeCoachResponse> GetCoachViewAsync(string? seasonLabel, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeAdminResponse> SaveDivisionAliasAsync(FieldInventoryDivisionAliasSaveRequest request, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeAdminResponse> SaveTeamAliasAsync(FieldInventoryTeamAliasSaveRequest request, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeAdminResponse> SaveGroupPolicyAsync(FieldInventoryGroupPolicySaveRequest request, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeNormalizeResponse> NormalizeAvailabilityAsync(FieldInventoryPracticeNormalizeRequest request, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeCoachResponse> CreatePracticeRequestAsync(FieldInventoryPracticeRequestCreateRequest request, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeCoachResponse> MovePracticeRequestAsync(string requestId, FieldInventoryPracticeRequestMoveRequest request, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeAdminResponse> ApprovePracticeRequestAsync(string requestId, FieldInventoryPracticeRequestDecisionRequest request, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeAdminResponse> RejectPracticeRequestAsync(string requestId, FieldInventoryPracticeRequestDecisionRequest request, string userId, CorrelationContext context);
    Task<FieldInventoryPracticeCoachResponse> CancelPracticeRequestAsync(string requestId, string userId, CorrelationContext context);
    Task<PracticeConflictCheckResponse> CheckMoveConflictsAsync(string seasonLabel, string practiceSlotKey, string userId, CorrelationContext context);
}
