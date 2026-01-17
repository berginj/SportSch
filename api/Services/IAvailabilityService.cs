using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service interface for field availability rules and exceptions.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Creates a new availability rule.
    /// </summary>
    Task<object> CreateRuleAsync(CreateAvailabilityRuleRequest request, CorrelationContext context);

    /// <summary>
    /// Updates an existing availability rule.
    /// </summary>
    Task<object> UpdateRuleAsync(UpdateAvailabilityRuleRequest request, CorrelationContext context);

    /// <summary>
    /// Deactivates an availability rule (soft delete).
    /// </summary>
    Task<object> DeactivateRuleAsync(string leagueId, string ruleId, string userId);

    /// <summary>
    /// Gets all availability rules for a specific field.
    /// </summary>
    Task<List<object>> GetRulesAsync(string leagueId, string fieldKey);

    /// <summary>
    /// Creates a new exception for an availability rule.
    /// </summary>
    Task<object> CreateExceptionAsync(CreateAvailabilityExceptionRequest request, CorrelationContext context);

    /// <summary>
    /// Updates an existing exception for an availability rule.
    /// </summary>
    Task<object> UpdateExceptionAsync(UpdateAvailabilityExceptionRequest request, CorrelationContext context);

    /// <summary>
    /// Deletes an exception for an availability rule.
    /// </summary>
    Task DeleteExceptionAsync(string leagueId, string ruleId, string exceptionId, string userId);

    /// <summary>
    /// Lists all exceptions for a specific availability rule.
    /// </summary>
    Task<List<object>> ListExceptionsAsync(string leagueId, string ruleId);

    /// <summary>
    /// Previews availability slots based on active rules and exceptions.
    /// </summary>
    Task<List<object>> PreviewSlotsAsync(string leagueId, DateOnly dateFrom, DateOnly dateTo);
}

/// <summary>
/// Request DTO for creating an availability rule.
/// </summary>
public class CreateAvailabilityRuleRequest
{
    public required string LeagueId { get; init; }
    public string? RuleId { get; init; }
    public required string FieldKey { get; init; }
    public required string Division { get; init; }
    public List<string> DivisionIds { get; init; } = new();
    public required string StartsOn { get; init; }
    public required string EndsOn { get; init; }
    public List<string> DaysOfWeek { get; init; } = new();
    public required string StartTimeLocal { get; init; }
    public required string EndTimeLocal { get; init; }
    public string RecurrencePattern { get; init; } = "Weekly";
    public string Timezone { get; init; } = "America/New_York";
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Request DTO for updating an availability rule.
/// </summary>
public class UpdateAvailabilityRuleRequest
{
    public required string LeagueId { get; init; }
    public required string RuleId { get; init; }
    public required string FieldKey { get; init; }
    public required string Division { get; init; }
    public List<string> DivisionIds { get; init; } = new();
    public required string StartsOn { get; init; }
    public required string EndsOn { get; init; }
    public List<string> DaysOfWeek { get; init; } = new();
    public required string StartTimeLocal { get; init; }
    public required string EndTimeLocal { get; init; }
    public string RecurrencePattern { get; init; } = "Weekly";
    public string Timezone { get; init; } = "America/New_York";
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Request DTO for creating an availability exception.
/// </summary>
public class CreateAvailabilityExceptionRequest
{
    public required string LeagueId { get; init; }
    public required string RuleId { get; init; }
    public string? ExceptionId { get; init; }
    public required string DateFrom { get; init; }
    public required string DateTo { get; init; }
    public required string StartTimeLocal { get; init; }
    public required string EndTimeLocal { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>
/// Request DTO for updating an availability exception.
/// </summary>
public class UpdateAvailabilityExceptionRequest
{
    public required string LeagueId { get; init; }
    public required string RuleId { get; init; }
    public required string ExceptionId { get; init; }
    public required string DateFrom { get; init; }
    public required string DateTo { get; init; }
    public required string StartTimeLocal { get; init; }
    public required string EndTimeLocal { get; init; }
    public string Reason { get; init; } = "";
}
