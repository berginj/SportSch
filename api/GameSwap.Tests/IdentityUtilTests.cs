#nullable enable
using System;
using System.Text;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using Xunit;

namespace GameSwap.Tests;

public class IdentityUtilTests
{
    [Fact]
    public void GetMe_IgnoresSpoofedDevHeadersOnNonLocalRequests()
    {
        var previous = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT");
        Environment.SetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT", "Production");
        try
        {
            var request = CreateRequest(
                "https://sportsch.example.com/api/me",
                new HttpHeadersCollection
                {
                    { "x-user-id", "spoofed-user" },
                    { "x-user-email", "spoofed@example.com" },
                });

            var me = IdentityUtil.GetMe(request.Object);

            Assert.Equal("UNKNOWN", me.UserId);
            Assert.Equal("UNKNOWN", me.Email);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT", previous);
        }
    }

    [Fact]
    public void GetMe_AllowsDevHeadersForLocalhostRequests()
    {
        var previous = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT");
        Environment.SetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT", null);
        try
        {
            var request = CreateRequest(
                "http://localhost:7071/api/me",
                new HttpHeadersCollection
                {
                    { "x-user-id", "local-user" },
                    { "x-user-email", "local@example.com" },
                });

            var me = IdentityUtil.GetMe(request.Object);

            Assert.Equal("local-user", me.UserId);
            Assert.Equal("local@example.com", me.Email);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT", previous);
        }
    }

    [Fact]
    public void GetAuthenticatedUserId_ReadsClientPrincipalOnly()
    {
        var request = CreateRequest(
            "https://sportsch.example.com/api/me",
            new HttpHeadersCollection
            {
                { "x-ms-client-principal", BuildClientPrincipal("auth-user", "auth@example.com") },
                { "x-user-id", "spoofed-user" },
            });

        var userId = IdentityUtil.GetAuthenticatedUserId(request.Object);

        Assert.Equal("auth-user", userId);
    }

    private static Mock<HttpRequestData> CreateRequest(string url, HttpHeadersCollection headers)
    {
        var context = new Mock<FunctionContext>();
        var request = new Mock<HttpRequestData>(context.Object);
        request.SetupGet(r => r.Headers).Returns(headers);
        request.SetupGet(r => r.Url).Returns(new Uri(url));
        return request;
    }

    private static string BuildClientPrincipal(string userId, string email)
    {
        var json = $$"""
        {"userId":"{{userId}}","userDetails":"{{email}}","claims":[]}
        """;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
