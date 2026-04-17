#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using GameSwap.Functions.Middleware;
using GameSwap.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameSwap.Tests;

public class RateLimitingMiddlewareTests
{
    [Fact]
    public void GetIdentifier_UsesAuthenticatedClientPrincipalBeforeForwardedIp()
    {
        var logger = new Mock<ILogger<RateLimitingMiddleware>>();
        var rateLimitService = new Mock<IRateLimitService>();
        var functionContext = new Mock<FunctionContext>();
        var request = new Mock<HttpRequestData>(functionContext.Object);
        var headers = new HttpHeadersCollection
        {
            { "X-Forwarded-For", "198.51.100.10, 203.0.113.20, 10.0.0.5" },
            { "x-ms-client-principal", BuildClientPrincipal("auth-user-123", "user@example.com") },
            { "x-user-id", "spoofed-user" }
        };
        request.SetupGet(r => r.Headers).Returns(headers);

        var middleware = new RateLimitingMiddleware(logger.Object, rateLimitService.Object);
        var method = typeof(RateLimitingMiddleware).GetMethod("GetIdentifier", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetIdentifier method not found.");

        var identifier = (string?)method.Invoke(middleware, new object[] { request.Object });

        Assert.Equal("user:auth-user-123", identifier);
    }

    [Fact]
    public void GetIdentifier_IgnoresSpoofableUserHeaderAndUsesLeftmostForwardedIp()
    {
        var logger = new Mock<ILogger<RateLimitingMiddleware>>();
        var rateLimitService = new Mock<IRateLimitService>();
        var functionContext = new Mock<FunctionContext>();
        var request = new Mock<HttpRequestData>(functionContext.Object);
        var headers = new HttpHeadersCollection
        {
            { "X-Forwarded-For", "198.51.100.10, 203.0.113.20, 10.0.0.5" },
            { "x-user-id", "spoofed-user" }
        };
        request.SetupGet(r => r.Headers).Returns(headers);

        var middleware = new RateLimitingMiddleware(logger.Object, rateLimitService.Object);
        var method = typeof(RateLimitingMiddleware).GetMethod("GetIdentifier", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetIdentifier method not found.");

        var identifier = (string?)method.Invoke(middleware, new object[] { request.Object });

        Assert.Equal("ip:198.51.100.10", identifier);
    }

    [Fact]
    public void AddRateLimitHeaders_WritesHeadersToResponse()
    {
        var logger = new Mock<ILogger<RateLimitingMiddleware>>();
        var rateLimitService = new Mock<IRateLimitService>();
        var functionContext = new Mock<FunctionContext>();
        var responseHeaders = new HttpHeadersCollection();
        var response = new Mock<HttpResponseData>(functionContext.Object);
        response.SetupGet(r => r.Headers).Returns(responseHeaders);

        var middleware = new RateLimitingMiddleware(logger.Object, rateLimitService.Object);
        var isAllowed = typeof(RateLimitingMiddleware).GetMethod("IsAllowed", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("IsAllowed method not found.");
        var addRateLimitHeaders = typeof(RateLimitingMiddleware).GetMethod("AddRateLimitHeaders", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("AddRateLimitHeaders method not found.");

        var identifier = $"ip:198.51.100.{Random.Shared.Next(200, 255)}";
        _ = isAllowed.Invoke(middleware, new object[] { identifier });
        addRateLimitHeaders.Invoke(middleware, new object[] { response.Object, identifier });

        Assert.Equal("100", GetHeader(responseHeaders, "X-RateLimit-Limit"));
        Assert.Equal("99", GetHeader(responseHeaders, "X-RateLimit-Remaining"));
        Assert.False(string.IsNullOrWhiteSpace(GetHeader(responseHeaders, "X-RateLimit-Reset")));
    }

    private static string BuildClientPrincipal(string userId, string email)
    {
        var json = $$"""
        {"userId":"{{userId}}","userDetails":"{{email}}","claims":[]}
        """;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static string GetHeader(HttpHeadersCollection headers, string name)
    {
        return headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault() ?? string.Empty
            : string.Empty;
    }
}
