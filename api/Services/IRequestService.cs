using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service interface for slot request business logic.
/// Handles creating requests, approving requests, and querying requests.
/// </summary>
public interface IRequestService
{
    /// <summary>
    /// Creates a request for a slot (immediately confirms it).
    /// </summary>
    Task<object> CreateRequestAsync(CreateRequestRequest request, CorrelationContext context);

    /// <summary>
    /// Approves a pending request for a slot.
    /// </summary>
    Task<object> ApproveRequestAsync(ApproveRequestRequest request, CorrelationContext context);

    /// <summary>
    /// Gets all requests for a specific slot.
    /// </summary>
    Task<List<object>> QueryRequestsAsync(string leagueId, string division, string slotId);
}

/// <summary>
/// Request DTO for creating a slot request.
/// </summary>
public class CreateRequestRequest
{
    public required string LeagueId { get; init; }
    public required string Division { get; init; }
    public required string SlotId { get; init; }
    public string Notes { get; init; } = "";
    public string? RequestingTeamId { get; init; }
    public string? RequestingDivision { get; init; }
}

/// <summary>
/// Request DTO for approving a slot request.
/// </summary>
public class ApproveRequestRequest
{
    public required string LeagueId { get; init; }
    public required string Division { get; init; }
    public required string SlotId { get; init; }
    public required string RequestId { get; init; }
    public string ApprovedByEmail { get; init; } = "";
}
