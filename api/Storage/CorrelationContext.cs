using Microsoft.Azure.Functions.Worker.Http;
using Azure.Data.Tables;

namespace GameSwap.Functions.Storage;

/// <summary>
/// Context for tracking requests across service boundaries.
/// Includes correlation ID for distributed tracing and user/league context.
/// Provides request-scoped caching for frequently accessed data.
/// </summary>
public class CorrelationContext
{
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public string? UserEmail { get; set; }
    public string LeagueId { get; set; } = "";

    // Request-scoped cache for membership lookups to avoid redundant queries
    private Dictionary<string, TableEntity?> _membershipCache = new();
    
    /// <summary>
    /// Gets or sets a cached membership entity. Lookups within a request reuse cached results.
    /// </summary>
    public TableEntity? GetCachedMembership(string userId, string leagueId)
    {
        var key = $"{userId}:{leagueId}";
        _membershipCache.TryGetValue(key, out var cached);
        return cached;
    }

    /// <summary>
    /// Caches a membership entity for reuse within this request.
    /// </summary>
    public void SetCachedMembership(string userId, string leagueId, TableEntity? membership)
    {
        var key = $"{userId}:{leagueId}";
        _membershipCache[key] = membership;
    }

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
