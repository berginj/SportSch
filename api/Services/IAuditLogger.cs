using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

/// <summary>
/// Service for logging audit events for security and compliance.
/// Tracks sensitive operations like role changes, data exports, and bulk operations.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs a slot creation event.
    /// </summary>
    void LogSlotCreated(string userId, string leagueId, string slotId, string division, string correlationId);

    /// <summary>
    /// Logs a slot cancellation event.
    /// </summary>
    void LogSlotCancelled(string userId, string leagueId, string slotId, string reason, string correlationId);

    /// <summary>
    /// Logs a slot request approval event.
    /// </summary>
    void LogRequestApproved(string userId, string leagueId, string slotId, string requestId, string requestingUserId, string correlationId);

    /// <summary>
    /// Logs a slot request denial event.
    /// </summary>
    void LogRequestDenied(string userId, string leagueId, string slotId, string requestId, string requestingUserId, string reason, string correlationId);

    /// <summary>
    /// Logs a role change event (e.g., Viewer → Coach → LeagueAdmin).
    /// </summary>
    void LogRoleChange(string performedBy, string targetUserId, string leagueId, string oldRole, string newRole, string reason, string correlationId);

    /// <summary>
    /// Logs a bulk operation (e.g., bulk approve, bulk import).
    /// </summary>
    void LogBulkOperation(string userId, string leagueId, string operationType, int recordCount, string correlationId);

    /// <summary>
    /// Logs a data export event.
    /// </summary>
    void LogDataExport(string userId, string leagueId, string exportType, int recordCount, string correlationId);

    /// <summary>
    /// Logs a field creation or modification event.
    /// </summary>
    void LogFieldModified(string userId, string leagueId, string fieldKey, string operation, string correlationId);

    /// <summary>
    /// Logs a membership approval event.
    /// </summary>
    void LogMembershipApproved(string performedBy, string targetUserId, string leagueId, string role, string correlationId);

    /// <summary>
    /// Logs a membership denial event.
    /// </summary>
    void LogMembershipDenied(string performedBy, string targetUserId, string leagueId, string reason, string correlationId);

    /// <summary>
    /// Logs a configuration change event.
    /// </summary>
    void LogConfigurationChange(string userId, string leagueId, string settingName, string oldValue, string newValue, string correlationId);
}
