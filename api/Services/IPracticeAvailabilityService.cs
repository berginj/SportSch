using GameSwap.Functions.Models;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

public interface IPracticeAvailabilityService
{
    Task<PracticeAvailabilityOptionsResponse> GetCoachAvailabilityOptionsAsync(
        PracticeAvailabilityQueryRequest request,
        string userId,
        CorrelationContext context);

    Task<PracticeAvailabilityCheckResponse> CheckCoachAvailabilityAsync(
        PracticeAvailabilityQueryRequest request,
        string userId,
        CorrelationContext context);
}
