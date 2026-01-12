using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Telemetry;

public static class UsageTelemetry
{
    public static void Track(
        ILogger log,
        string eventName,
        string leagueId,
        string userId,
        object? props = null)
    {
        if (log == null || string.IsNullOrWhiteSpace(eventName)) return;

        var payload = props == null ? "" : JsonSerializer.Serialize(props);
        log.LogInformation("usage_event {EventName} {LeagueId} {UserId} {Props}", eventName, leagueId, userId, payload);
    }
}
