using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Middleware;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.UseMiddleware<RequestTelemetryMiddleware>();
        // Register rate limiting middleware
        builder.UseMiddleware<RateLimitingMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.Configure<LoggerFilterOptions>(options => options.Rules.Add(new LoggerFilterRule(
            "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider",
            "GameSwap.Functions", LogLevel.Information, null)));
        services.AddHttpClient();
        services.AddSingleton(TimeProvider.System);

        // Table Storage client
        var tableServiceClient = GameSwap.Functions.Storage.TableClients.CreateServiceClient(context.Configuration);
        services.AddSingleton(tableServiceClient);

        // Rate Limiting Service (singleton for local low-cost MVP stack)
        services.AddSingleton<IRateLimitService, InMemoryRateLimitService>();

        // Register Repositories (scoped for per-request lifetime)
        services.AddScoped<ISlotRepository, SlotRepository>();
        services.AddScoped<IFieldRepository, FieldRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<IPracticeRequestRepository, PracticeRequestRepository>();
        services.AddScoped<IGameRescheduleRequestRepository, GameRescheduleRequestRepository>();
        services.AddScoped<IDivisionRepository, DivisionRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IAccessRequestRepository, AccessRequestRepository>();
        services.AddScoped<ILeagueRepository, LeagueRepository>();
        services.AddScoped<IScheduleRunRepository, ScheduleRunRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationPreferencesRepository, NotificationPreferencesRepository>();
        services.AddScoped<IFieldInventoryImportRepository, FieldInventoryImportRepository>();

        // Umpire Management Repositories
        services.AddScoped<IUmpireProfileRepository, UmpireProfileRepository>();
        services.AddScoped<IUmpireAvailabilityRepository, UmpireAvailabilityRepository>();
        services.AddScoped<IGameUmpireAssignmentRepository, GameUmpireAssignmentRepository>();

        // Register Services (scoped for per-request lifetime)
        services.AddScoped<ISlotService, SlotService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<IPracticeRequestService, PracticeRequestService>();
        services.AddScoped<IGameRescheduleRequestService, GameRescheduleRequestService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationPreferencesService, NotificationPreferencesService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IFieldInventoryImportService, FieldInventoryImportService>();
        services.AddScoped<IFieldInventoryPracticeService, FieldInventoryPracticeService>();
        services.AddScoped<IPracticeAvailabilityService, PracticeAvailabilityService>();

        // Umpire Management Services
        services.AddScoped<IUmpireService, UmpireService>();
        services.AddScoped<IUmpireAssignmentService, UmpireAssignmentService>();
        services.AddScoped<UmpireNotificationService>();

        // Table creation on startup (if configured)
        if (context.Configuration.GetValue<bool>("GAMESWAP_CREATE_TABLES", true))
        {
            services.AddHostedService<GameSwap.Functions.Storage.TableStartup>();
        }
    })
    .Build();

host.Run();
