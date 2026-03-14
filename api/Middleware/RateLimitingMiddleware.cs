using System.Collections.Concurrent;
using System.Net;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Middleware;

/// <summary>
/// Rate limiting middleware using sliding window algorithm.
/// Tracks requests per user and enforces configurable rate limits.
/// </summary>
public class RateLimitingMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<RateLimitingMiddleware> _logger;

    // In-memory storage for rate limiting (for distributed systems, use Redis)
    private static readonly ConcurrentDictionary<string, RequestWindow> _requestWindows = new();

    // Rate limit configuration
    private const int MaxRequestsPerMinute = 100;
    private const int WindowSizeSeconds = 60;

    public RateLimitingMiddleware(ILogger<RateLimitingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData == null)
        {
            await next(context);
            return;
        }

        // Get identifier (user ID or IP address)
        var identifier = GetIdentifier(requestData);

        // Check rate limit
        if (!IsAllowed(identifier))
        {
            _logger.LogWarning("Rate limit exceeded for {Identifier}", identifier);

            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Content-Type", "application/json");
            response.Headers.Add("Retry-After", "60");
            response.Headers.Add("X-RateLimit-Limit", MaxRequestsPerMinute.ToString());
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", GetResetTime(identifier).ToString());

            await response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "RATE_LIMIT_EXCEEDED",
                    message = $"Rate limit exceeded. Maximum {MaxRequestsPerMinute} requests per minute allowed."
                }
            });

            context.GetInvocationResult().Value = response;
            return;
        }

        await next(context);

        if (context.GetInvocationResult().Value is HttpResponseData outgoingResponse)
        {
            AddRateLimitHeaders(outgoingResponse, identifier);
        }

        // Clean up old windows periodically
        CleanupOldWindows();
    }

    private string GetIdentifier(HttpRequestData req)
    {
        // Prefer authenticated SWA/EasyAuth identity only; do not trust client-supplied dev headers here.
        var authenticatedUserId = IdentityUtil.GetAuthenticatedUserId(req);
        if (!string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            return $"user:{authenticatedUserId}";
        }

        // X-Forwarded-For is ordered client -> proxy1 -> proxy2.
        // Use the leftmost IP so rate limiting tracks the originating client instead of the nearest proxy.
        if (req.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
        {
            var chainStr = forwardedFor.FirstOrDefault() ?? "";
            var ips = chainStr.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            
            if (ips.Count > 0)
            {
                var ip = ips[0];
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return $"ip:{ip}";
                }
            }
        }

        return "ip:unknown";
    }

    private bool IsAllowed(string identifier)
    {
        var now = DateTimeOffset.UtcNow;
        var window = _requestWindows.GetOrAdd(identifier, _ => new RequestWindow());

        lock (window)
        {
            // Remove requests outside the window
            window.Requests.RemoveAll(r => r < now.AddSeconds(-WindowSizeSeconds));

            // Check if limit exceeded
            if (window.Requests.Count >= MaxRequestsPerMinute)
            {
                return false;
            }

            // Add current request
            window.Requests.Add(now);
            return true;
        }
    }

    private void AddRateLimitHeaders(HttpResponseData response, string identifier)
    {
        if (_requestWindows.TryGetValue(identifier, out var window))
        {
            lock (window)
            {
                var remaining = Math.Max(0, MaxRequestsPerMinute - window.Requests.Count);
                response.Headers.Add("X-RateLimit-Limit", MaxRequestsPerMinute.ToString());
                response.Headers.Add("X-RateLimit-Remaining", remaining.ToString());
                response.Headers.Add("X-RateLimit-Reset", GetResetTime(identifier).ToString());
            }
        }
    }

    private long GetResetTime(string identifier)
    {
        if (_requestWindows.TryGetValue(identifier, out var window))
        {
            lock (window)
            {
                if (window.Requests.Count > 0)
                {
                    var oldestRequest = window.Requests.Min();
                    var resetTime = oldestRequest.AddSeconds(WindowSizeSeconds);
                    return new DateTimeOffset(resetTime.DateTime, TimeSpan.Zero).ToUnixTimeSeconds();
                }
            }
        }
        return DateTimeOffset.UtcNow.AddSeconds(WindowSizeSeconds).ToUnixTimeSeconds();
    }

    private void CleanupOldWindows()
    {
        // Only cleanup occasionally to avoid overhead
        if (Random.Shared.Next(100) > 5) return;

        var now = DateTimeOffset.UtcNow;
        var keysToRemove = new List<string>();

        foreach (var kvp in _requestWindows)
        {
            lock (kvp.Value)
            {
                kvp.Value.Requests.RemoveAll(r => r < now.AddSeconds(-WindowSizeSeconds * 2));
                if (kvp.Value.Requests.Count == 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            _requestWindows.TryRemove(key, out _);
        }
    }

    private class RequestWindow
    {
        public List<DateTimeOffset> Requests { get; } = new();
    }
}
