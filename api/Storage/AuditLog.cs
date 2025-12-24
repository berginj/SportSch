using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace GameSwap.Functions.Storage;

public static class AuditLog
{
    private const string TableName = "GameSwapAuditLog";

    // Primary signature — use this for new calls
    public static async Task WriteAsync(
        TableServiceClient svc,
        string actorUserId,
        string actorEmail,
        string action,
        string result,
        string? leagueId = null,
        string? targetType = null,
        string? targetId = null,
        string? targetUserId = null,
        string? targetEmail = null,
        string? ip = null,
        object? details = null)
    {
        if (string.IsNullOrWhiteSpace(actorUserId)) actorUserId = "UNKNOWN";

        // Canonicalize target identifiers
        if (string.IsNullOrWhiteSpace(targetId) && !string.IsNullOrWhiteSpace(targetUserId))
        {
            targetId = targetUserId;
            targetType ??= "User";
        }

        var table = await TableClients.GetTableAsync(svc, TableName);
        var now = DateTimeOffset.UtcNow;
        var pk = $"AUDIT#{now:yyyy-MM-dd}";
        var rk = $"{now:HHmmss.fffffff}#{Guid.NewGuid():N}";

        var entity = new TableEntity(pk, rk)
        {
            ["AtUtc"] = now,
            ["ActorUserId"] = actorUserId,
            ["ActorEmail"] = actorEmail ?? "",
            ["Action"] = action ?? "",
            ["Result"] = result ?? "",
            ["LeagueId"] = leagueId ?? "",
            ["TargetType"] = targetType ?? "",
            ["TargetId"] = targetId ?? "",
            ["TargetEmail"] = targetEmail ?? "",
            ["Ip"] = ip ?? "",
            ["DetailsJson"] = details is null ? "" : JsonSerializer.Serialize(details)
        };

        await table.AddEntityAsync(entity);
    }
}
