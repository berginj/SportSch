using Microsoft.Azure.Functions.Worker.Http;

namespace GameSwap.Functions.Storage;

/// <summary>
/// Context for tracking requests across service boundaries.
/// Includes correlation ID for distributed tracing and user/league context.
/// </summary>
public class CorrelationContext
{
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public string? UserEmail { get; set; }
    public string LeagueId { get; set; } = "";

    /// <summary>
    /// Creates a correlation context from an HTTP request.
    /// </summary>
    public static CorrelationContext FromRequest(HttpRequestData req, string? leagueId = null)
    {
        var me = IdentityUtil.GetMe(req);

        // Extract correlation ID from header or generate new one
        var correlationId = req.Headers.TryGetValues("x-correlation-id", out var vals)
            ? vals.FirstOrDefault()
            : null;

        return new CorrelationContext
        {
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            UserId = me.UserId,
            UserEmail = me.Email,
            LeagueId = leagueId ?? ""
        };
    }

    /// <summary>
    /// Creates a correlation context with explicit values.
    /// </summary>
    public static CorrelationContext Create(string userId, string leagueId, string? correlationId = null)
    {
        return new CorrelationContext
        {
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            UserId = userId,
            LeagueId = leagueId
        };
    }
}
