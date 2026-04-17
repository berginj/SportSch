namespace GameSwap.Functions.Services;

/// <summary>
/// Service for distributed rate limiting using Redis.
/// Implements a sliding window algorithm to track request rates across multiple instances.
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Checks if a request is allowed based on rate limits.
    /// </summary>
    /// <param name="identifier">Unique identifier (user ID or IP address)</param>
    /// <returns>True if request is allowed, false if rate limit exceeded</returns>
    Task<bool> IsAllowedAsync(string identifier);

    /// <summary>
    /// Gets the current rate limit status for an identifier.
    /// </summary>
    /// <param name="identifier">Unique identifier (user ID or IP address)</param>
    /// <returns>Rate limit information including remaining requests and reset time</returns>
    Task<RateLimitInfo> GetRateLimitInfoAsync(string identifier);
}

/// <summary>
/// Rate limit information for a specific identifier.
/// </summary>
public record RateLimitInfo(
    int Limit,
    int Remaining,
    long ResetTimestampSeconds
);
