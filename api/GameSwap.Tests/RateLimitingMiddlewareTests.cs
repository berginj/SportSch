#nullable enable
using System;
using System.Reflection;
using System.Text;
using GameSwap.Functions.Middleware;
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
        var functionContext = new Mock<FunctionContext>();
        var request = new Mock<HttpRequestData>(functionContext.Object);
        var headers = new HttpHeadersCollection
        {
            { "X-Forwarded-For", "198.51.100.10, 203.0.113.20, 10.0.0.5" },
            { "x-ms-client-principal", BuildClientPrincipal("auth-user-123", "user@example.com") },
            { "x-user-id", "spoofed-user" }
        };
        request.SetupGet(r => r.Headers).Returns(headers);

        var middleware = new RateLimitingMiddleware(logger.Object);
        var method = typeof(RateLimitingMiddleware).GetMethod("GetIdentifier", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetIdentifier method not found.");

        var identifier = (string?)method.Invoke(middleware, new object[] { request.Object });

        Assert.Equal("user:auth-user-123", identifier);
    }

    [Fact]
    public void GetIdentifier_IgnoresSpoofableUserHeaderAndUsesLeftmostForwardedIp()
    {
        var logger = new Mock<ILogger<RateLimitingMiddleware>>();
        var functionContext = new Mock<FunctionContext>();
        var request = new Mock<HttpRequestData>(functionContext.Object);
        var headers = new HttpHeadersCollection
        {
            { "X-Forwarded-For", "198.51.100.10, 203.0.113.20, 10.0.0.5" },
            { "x-user-id", "spoofed-user" }
        };
        request.SetupGet(r => r.Headers).Returns(headers);

        var middleware = new RateLimitingMiddleware(logger.Object);
        var method = typeof(RateLimitingMiddleware).GetMethod("GetIdentifier", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetIdentifier method not found.");

        var identifier = (string?)method.Invoke(middleware, new object[] { request.Object });

        Assert.Equal("ip:198.51.100.10", identifier);
    }

    private static string BuildClientPrincipal(string userId, string email)
    {
        var json = $$"""
        {"userId":"{{userId}}","userDetails":"{{email}}","claims":[]}
        """;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
