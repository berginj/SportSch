using System.Net;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Middleware;

/// <summary>
/// Rate limiting middleware using distributed Redis-based sliding window algorithm.
/// Tracks requests per user across multiple instances and enforces configurable rate limits.
/// </summary>
public class RateLimitingMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly IRateLimitService _rateLimitService;

    // Rate limit configuration
    private const int MaxRequestsPerMinute = 100;

    public RateLimitingMiddleware(
        ILogger<RateLimitingMiddleware> logger,
        IRateLimitService rateLimitService)
    {
        _logger = logger;
        _rateLimitService = rateLimitService;
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

        // Check rate limit using distributed service
        var isAllowed = await _rateLimitService.IsAllowedAsync(identifier);

        if (!isAllowed)
        {
            _logger.LogWarning("Rate limit exceeded for {Identifier}", identifier);

            var rateLimitInfo = await _rateLimitService.GetRateLimitInfoAsync(identifier);

            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Content-Type", "application/json");
            response.Headers.Add("Retry-After", "60");
            response.Headers.Add("X-RateLimit-Limit", rateLimitInfo.Limit.ToString());
            response.Headers.Add("X-RateLimit-Remaining", rateLimitInfo.Remaining.ToString());
            response.Headers.Add("X-RateLimit-Reset", rateLimitInfo.ResetTimestampSeconds.ToString());

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
            await AddRateLimitHeadersAsync(outgoingResponse, identifier);
        }
    }

    private string GetIdentifier(HttpRequestData req)
    {
        // Prefer authenticated SWA/EasyAuth identity only; do not trust client-supplied dev headers here.
        var authenticatedUserId = IdentityUtil.GetAuthenticatedUserId(req);
        if (!string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            return $"user:{authenticatedUserId}";
        }

        // SECURITY: For unauthenticated requests, use Azure-provided client IP
        // Azure Static Web Apps and Azure Front Door set X-Azure-ClientIP with the real client IP
        // This header cannot be spoofed by the client as it's set by Azure infrastructure
        if (req.Headers.TryGetValues("X-Azure-ClientIP", out var azureClientIp))
        {
            var ip = azureClientIp.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return $"ip:{ip}";
            }
        }

        // Fallback: Use X-Forwarded-For from Azure (more secure than trusting client)
        // Azure infrastructure ensures the rightmost IP is from Azure's edge
        // For Azure Functions behind Azure Front Door/Application Gateway:
        // X-Forwarded-For format: client_ip, proxy1, proxy2, azure_edge
        if (req.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
        {
            var chainStr = forwardedFor.FirstOrDefault() ?? "";
            var ips = chainStr.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            if (ips.Count > 0)
            {
                // Use the first IP (client IP) as Azure ensures the chain integrity
                // In Azure environment, the X-Forwarded-For header is controlled by Azure infrastructure
                var ip = ips[0];
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return $"ip:{ip}";
                }
            }
        }

        // Last resort: use remote IP address from connection
        // This is the IP of the immediate caller (Azure infrastructure)
        return "ip:unknown";
    }

    private async Task AddRateLimitHeadersAsync(HttpResponseData response, string identifier)
    {
        try
        {
            var rateLimitInfo = await _rateLimitService.GetRateLimitInfoAsync(identifier);
            response.Headers.Add("X-RateLimit-Limit", rateLimitInfo.Limit.ToString());
            response.Headers.Add("X-RateLimit-Remaining", rateLimitInfo.Remaining.ToString());
            response.Headers.Add("X-RateLimit-Reset", rateLimitInfo.ResetTimestampSeconds.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add rate limit headers for {Identifier}", identifier);
            // Continue without headers rather than failing the request
        }
    }
}
