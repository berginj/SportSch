using System;
using System.Collections.Generic;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GameSwap.Tests.Integration;

/// <summary>
/// Base class for integration tests providing DI setup and mock infrastructure.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected IServiceProvider Services { get; }
    protected Mock<TableServiceClient> MockTableService { get; }

    protected IntegrationTestBase()
    {
        var services = new ServiceCollection();

        // Mock configuration
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureWebJobsStorage", "UseDevelopmentStorage=true" },
                { "SendGridKey", "test-key" }
            })
            .Build();

        // Mock TableServiceClient
        MockTableService = new Mock<TableServiceClient>();
        services.AddSingleton(MockTableService.Object);

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register repositories
        services.AddScoped<ISlotRepository, SlotRepository>();
        services.AddScoped<IFieldRepository, FieldRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();

        // Register services
        services.AddScoped<ISlotService, SlotService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();

        Services = services.BuildServiceProvider();
    }

    protected T GetService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    protected CorrelationContext CreateContext(string userId, string leagueId, string? correlationId = null)
    {
        return new CorrelationContext
        {
            UserId = userId,
            LeagueId = leagueId,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString()
        };
    }

    public virtual void Dispose()
    {
        (Services as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }
}
