using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Services;

/// <summary>
/// Implementation of audit logging service using structured logging.
/// All audit logs are written with consistent format for easy querying in Application Insights.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(ILogger<AuditLogger> logger)
    {
        _logger = logger;
    }

    public void LogSlotCreated(string userId, string leagueId, string slotId, string division, string correlationId)
    {
        _logger.LogInformation(
            "AUDIT: Slot created. User={UserId}, League={LeagueId}, Slot={SlotId}, Division={Division}, Correlation={CorrelationId}",
            userId, leagueId, slotId, division, correlationId);
    }

    public void LogSlotCancelled(string userId, string leagueId, string slotId, string reason, string correlationId)
    {
        _logger.LogInformation(
            "AUDIT: Slot cancelled. User={UserId}, League={LeagueId}, Slot={SlotId}, Reason={Reason}, Correlation={CorrelationId}",
            userId, leagueId, slotId, reason, correlationId);
    }

    public void LogRequestApproved(string userId, string leagueId, string slotId, string requestId, string requestingUserId, string correlationId)
    {
        _logger.LogInformation(
            "AUDIT: Request approved. ApprovedBy={UserId}, RequestingUser={RequestingUserId}, League={LeagueId}, Slot={SlotId}, Request={RequestId}, Correlation={CorrelationId}",
            userId, requestingUserId, leagueId, slotId, requestId, correlationId);
    }

    public void LogRequestDenied(string userId, string leagueId, string slotId, string requestId, string requestingUserId, string reason, string correlationId)
    {
        _logger.LogInformation(
            "AUDIT: Request denied. DeniedBy={UserId}, RequestingUser={RequestingUserId}, League={LeagueId}, Slot={SlotId}, Request={RequestId}, Reason={Reason}, Correlation={CorrelationId}",
            userId, requestingUserId, leagueId, slotId, requestId, reason, correlationId);
    }

    public void LogRoleChange(string performedBy, string targetUserId, string leagueId, string oldRole, string newRole, string reason, string correlationId)
    {
        _logger.LogWarning(
            "AUDIT: Role change. PerformedBy={PerformedBy}, TargetUser={TargetUserId}, League={LeagueId}, OldRole={OldRole}, NewRole={NewRole}, Reason={Reason}, Correlation={CorrelationId}",
            performedBy, targetUserId, leagueId, oldRole, newRole, reason, correlationId);
    }

    public void LogBulkOperation(string userId, string leagueId, string operationType, int recordCount, string correlationId)
    {
        _logger.LogWarning(
            "AUDIT: Bulk operation. User={UserId}, League={LeagueId}, Operation={OperationType}, RecordCount={RecordCount}, Correlation={CorrelationId}",
            userId, leagueId, operationType, recordCount, correlationId);
    }

    public void LogDataExport(string userId, string leagueId, string exportType, int recordCount, string correlationId)
    {
        _logger.LogWarning(
            "AUDIT: Data export. User={UserId}, League={LeagueId}, ExportType={ExportType}, RecordCount={RecordCount}, Correlation={CorrelationId}",
            userId, leagueId, exportType, recordCount, correlationId);
    }

    public void LogFieldModified(string userId, string leagueId, string fieldKey, string operation, string correlationId)
    {
        _logger.LogInformation(
            "AUDIT: Field modified. User={UserId}, League={LeagueId}, Field={FieldKey}, Operation={Operation}, Correlation={CorrelationId}",
            userId, leagueId, fieldKey, operation, correlationId);
    }

    public void LogMembershipApproved(string performedBy, string targetUserId, string leagueId, string role, string correlationId)
    {
        _logger.LogInformation(
            "AUDIT: Membership approved. ApprovedBy={PerformedBy}, TargetUser={TargetUserId}, League={LeagueId}, Role={Role}, Correlation={CorrelationId}",
            performedBy, targetUserId, leagueId, role, correlationId);
    }

    public void LogMembershipDenied(string performedBy, string targetUserId, string leagueId, string reason, string correlationId)
    {
        _logger.LogInformation(
            "AUDIT: Membership denied. DeniedBy={PerformedBy}, TargetUser={TargetUserId}, League={LeagueId}, Reason={Reason}, Correlation={CorrelationId}",
            performedBy, targetUserId, leagueId, reason, correlationId);
    }

    public void LogConfigurationChange(string userId, string leagueId, string settingName, string oldValue, string newValue, string correlationId)
    {
        _logger.LogWarning(
            "AUDIT: Configuration change. User={UserId}, League={LeagueId}, Setting={SettingName}, OldValue={OldValue}, NewValue={NewValue}, Correlation={CorrelationId}",
            userId, leagueId, settingName, oldValue, newValue, correlationId);
    }
}
