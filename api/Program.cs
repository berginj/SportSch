using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var tableServiceClient = GameSwap.Functions.Storage.TableClients.CreateServiceClient(context.Configuration);
        services.AddSingleton(tableServiceClient);
        if (context.Configuration.GetValue<bool>("GAMESWAP_CREATE_TABLES"))
        {
            services.AddHostedService<GameSwap.Functions.Storage.TableStartup>();
        }
    })
    .Build();

host.Run();
