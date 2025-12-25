using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var tableServiceClient = GameSwap.Functions.Storage.TableClients.CreateServiceClient(context.Configuration);
        services.AddSingleton(tableServiceClient);
        if (context.Configuration.GetValue<bool>("GAMESWAP_CREATE_TABLES"))
        {
            services.AddHostedService<GameSwap.Functions.Storage.TableStartup>();
        }
    })
    .Build();

host.Run();
