using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameSwap.Functions.Services;

/// <summary>
/// Redis-based distributed rate limiting service.
/// Uses sliding window algorithm with atomic Redis operations.
/// </summary>
public class RedisRateLimitService : IRateLimitService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisRateLimitService> _logger;
    private readonly bool _isEnabled;

    // Rate limit configuration
    private const int MaxRequestsPerMinute = 100;
    private const int WindowSizeSeconds = 60;

    public RedisRateLimitService(IConfiguration configuration, ILogger<RedisRateLimitService> logger)
    {
        _logger = logger;

        var redisConnectionString = configuration["REDIS_CONNECTION_STRING"]
            ?? configuration["Redis:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            try
            {
                _redis = ConnectionMultiplexer.Connect(redisConnectionString);
                _isEnabled = true;
                _logger.LogInformation("Redis rate limiting enabled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Redis. Rate limiting will use in-memory fallback.");
                _isEnabled = false;
            }
        }
        else
        {
            _logger.LogWarning("Redis not configured. Rate limiting will use in-memory fallback (not distributed).");
            _isEnabled = false;
        }
    }

    public async Task<bool> IsAllowedAsync(string identifier)
    {
        if (!_isEnabled || _redis == null)
        {
            // Fallback: allow all requests if Redis is not available
            // In production, you might want to use in-memory as fallback
            _logger.LogWarning("Redis unavailable, allowing request for {Identifier}", identifier);
            return true;
        }

        try
        {
            var db = _redis.GetDatabase();
            var key = $"ratelimit:{identifier}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var windowStart = now - WindowSizeSeconds;

            // Use Lua script for atomic operations
            var script = @"
                local key = KEYS[1]
                local now = tonumber(ARGV[1])
                local window_start = tonumber(ARGV[2])
                local max_requests = tonumber(ARGV[3])
                local window_size = tonumber(ARGV[4])

                -- Remove old entries outside the window
                redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)

                -- Count current requests in window
                local current = redis.call('ZCARD', key)

                if current < max_requests then
                    -- Add current request
                    redis.call('ZADD', key, now, now)
                    -- Set expiration to window size + buffer
                    redis.call('EXPIRE', key, window_size * 2)
                    return 1
                else
                    return 0
                end
            ";

            var result = await db.ScriptEvaluateAsync(
                script,
                new RedisKey[] { key },
                new RedisValue[] { now, windowStart, MaxRequestsPerMinute, WindowSizeSeconds }
            );

            return (int)result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis rate limit check failed for {Identifier}", identifier);
            // Fail open: allow request if Redis fails
            return true;
        }
    }

    public async Task<RateLimitInfo> GetRateLimitInfoAsync(string identifier)
    {
        if (!_isEnabled || _redis == null)
        {
            return new RateLimitInfo(
                Limit: MaxRequestsPerMinute,
                Remaining: MaxRequestsPerMinute,
                ResetTimestampSeconds: DateTimeOffset.UtcNow.AddSeconds(WindowSizeSeconds).ToUnixTimeSeconds()
            );
        }

        try
        {
            var db = _redis.GetDatabase();
            var key = $"ratelimit:{identifier}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var windowStart = now - WindowSizeSeconds;

            // Remove expired entries
            await db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, windowStart);

            // Get current count
            var currentCount = await db.SortedSetLengthAsync(key);
            var remaining = Math.Max(0, MaxRequestsPerMinute - (int)currentCount);

            // Get oldest request time for reset calculation
            var oldestEntries = await db.SortedSetRangeByScoreWithScoresAsync(key, take: 1);
            var resetTime = oldestEntries.Length > 0
                ? (long)oldestEntries[0].Score + WindowSizeSeconds
                : now + WindowSizeSeconds;

            return new RateLimitInfo(
                Limit: MaxRequestsPerMinute,
                Remaining: remaining,
                ResetTimestampSeconds: resetTime
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rate limit info for {Identifier}", identifier);
            return new RateLimitInfo(
                Limit: MaxRequestsPerMinute,
                Remaining: MaxRequestsPerMinute,
                ResetTimestampSeconds: DateTimeOffset.UtcNow.AddSeconds(WindowSizeSeconds).ToUnixTimeSeconds()
            );
        }
    }
}
