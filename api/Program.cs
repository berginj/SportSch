using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();

        // Table Storage client
        var tableServiceClient = GameSwap.Functions.Storage.TableClients.CreateServiceClient(context.Configuration);
        services.AddSingleton(tableServiceClient);

        // Register Repositories (scoped for per-request lifetime)
        services.AddScoped<ISlotRepository, SlotRepository>();
        services.AddScoped<IFieldRepository, FieldRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<IDivisionRepository, DivisionRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IAccessRequestRepository, AccessRequestRepository>();
        services.AddScoped<ILeagueRepository, LeagueRepository>();

        // Register Services (scoped for per-request lifetime)
        services.AddScoped<ISlotService, SlotService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();

        // Table creation on startup (if configured)
        if (context.Configuration.GetValue<bool>("GAMESWAP_CREATE_TABLES"))
        {
            services.AddHostedService<GameSwap.Functions.Storage.TableStartup>();
        }
    })
    .Build();

host.Run();
