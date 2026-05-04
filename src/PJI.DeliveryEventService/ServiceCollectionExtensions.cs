using Amazon.SQS;
using Asp.Versioning;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Authentication;
using PJI.DeliveryEventService.CloudApiClient;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Dispatcher;
using PJI.DeliveryEventService.Dispatcher.Processors;
using PJI.DeliveryEventService.Handlers.DroppedOff;
using PJI.DeliveryEventService.HealthChecks;
using PJI.DeliveryEventService.Infrastructure;
using PJI.DeliveryEventService.Messaging;
using PJI.DeliveryEventService.SecretsManager;
using PJI.DeliveryEventService.SecretsManager.Refresh;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService;

[ExcludeFromCodeCoverage]
internal static class ServiceCollectionExtensions
{
    internal static void AddServices(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddSecretsManager(builder.Configuration);

        AddSerilog(builder);

        var services = builder.Services;
        var configuration = builder.Configuration;

        AddSecretsRefresh(services, configuration);
        AddAwsServices(services, configuration);
        AddHmacAuthentication(services, configuration);
        AddApiVersioningAndControllers(services);
        AddHealthChecks(services);
        AddHandlers(services, configuration);
        AddMessaging(services, configuration);
        AddDispatcher(services);
        AddCloudApiClient(services, configuration);
        AddInfrastructure(services);
    }

    private static void AddSerilog(WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var logLevel = configuration.GetValue<LogEventLevel>("Serilog:MinimumLogLevel");

        var fileLogger = SerilogLoggerConfig(configuration, allowOverrides: true)
            .MinimumLevel.Is(logLevel)
            .WriteTo.File(
                new RenderedCompactJsonFormatter(),
                path: configuration.GetValue<string>("Serilog:PathAndFileNameBase")!,
                restrictedToMinimumLevel: logLevel,
                fileSizeLimitBytes: configuration.GetValue<long>("Serilog:FileSizeLimitBytes"),
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: configuration.GetValue<int>("Serilog:RetainedFileLimitCount"))
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(fileLogger);

        if (configuration.GetValue<bool>("Serilog:EnableConsoleLog"))
        {
            var consoleLogLevel = configuration.GetValue<LogEventLevel>("Serilog:MinimumLogLevelForConsole");

            var consoleLogger = SerilogLoggerConfig(configuration, allowOverrides: false)
                .MinimumLevel.Is(consoleLogLevel)
                .WriteTo.Console(
                    new RenderedCompactJsonFormatter(),
                    restrictedToMinimumLevel: consoleLogLevel)
                .CreateLogger();

            builder.Logging.AddSerilog(consoleLogger);
        }
    }

    private static LoggerConfiguration SerilogLoggerConfig(IConfiguration configuration, bool allowOverrides)
    {
        var loggerConfiguration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service.name", ServiceDetails.Name.ToLower())
            .Enrich.WithProperty("service.namespace", ServiceDetails.Namespace)
            .Enrich.WithProperty("service.version", configuration.GetValue<string>("Version"))
            .Enrich.WithProperty("service.instance.id", ServiceDetails.InstanceId);

        if (allowOverrides)
        {
            Dictionary<string, LogEventLevel> overrides = new();
            configuration.GetSection("Serilog:MinimumOverrideLogLevel").Bind(overrides);

            foreach (var keyValue in overrides)
            {
                loggerConfiguration.MinimumLevel.Override(keyValue.Key, keyValue.Value);
            }
        }

        return loggerConfiguration;
    }

    private static void AddSecretsRefresh(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SecretsManagerRefreshOptions>(o =>
            o.RefreshInterval = configuration.GetValue<TimeSpan>("SecretsRefreshInterval", TimeSpan.FromHours(1)));
        services.AddSingleton<ISecretsManagerRefreshHandler, SecretsManagerRefreshHandler>();
        services.AddHostedService<SecretsManagerRefreshService>();
    }

    private static void AddAwsServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonSQS>();
        services.AddAWSService<Amazon.SecretsManager.IAmazonSecretsManager>();
    }

    private static void AddHmacAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HmacClientsOptions>(configuration.GetSection(HmacClientsOptions.SectionName));
        services.AddAuthentication(HmacAuthenticationDefaults.SchemeName)
            .AddScheme<HmacAuthenticationSchemeOptions, HmacAuthenticationHandler>(
                HmacAuthenticationDefaults.SchemeName, _ => { });
        services.AddAuthorization();
    }

    private static void AddApiVersioningAndControllers(IServiceCollection services)
    {
        services.AddApiVersioning(o =>
        {
            o.DefaultApiVersion = new ApiVersion(1);
            o.AssumeDefaultVersionWhenUnspecified = true;
            o.ReportApiVersions = true;
            o.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddMvc()
        .AddApiExplorer(o =>
        {
            o.GroupNameFormat = "'v'V";
            o.SubstituteApiVersionInUrl = true;
        });

        services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
    }

    private static void AddHealthChecks(IServiceCollection services)
    {
        services.AddSingleton<DispatcherReadinessCheck>();
        services.AddHealthChecks()
            .AddCheck("liveness", () => HealthCheckResult.Healthy(), tags: [Tags.Health])
            .AddCheck<DispatcherReadinessCheck>("readiness", tags: [Tags.Readiness]);
    }

    private static void AddHandlers(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LocationOptions>(configuration.GetSection(LocationOptions.SectionName));
        services.AddScoped<IDroppedOffEventHandler, DroppedOffEventHandler>();
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SqsOptions>(configuration.GetSection("Sqs"));
        services.AddScoped<IDeliveryEventQueue, DeliveryEventQueue>();
    }

    private static void AddDispatcher(IServiceCollection services)
    {
        services.AddSingleton<IDeliveryEventProcessor, DroppedOffEventProcessor>();

        services.AddHostedService<DeliveryEventDispatcher>(sp =>
        {
            var sqsClient = sp.GetRequiredService<IAmazonSQS>();
            var sqsOptions = sp.GetRequiredService<IOptions<SqsOptions>>();

            // Resolve queue URL from queue name once at startup, before the dispatcher starts polling.
            if (string.IsNullOrEmpty(sqsOptions.Value.PersistenceQueueUrl))
            {
                sqsOptions.Value.PersistenceQueueUrl = sqsClient
                    .GetQueueUrlAsync(sqsOptions.Value.PersistenceQueueName)
                    .GetAwaiter().GetResult().QueueUrl;
            }

            return new DeliveryEventDispatcher(
                sp.GetServices<IDeliveryEventProcessor>(),
                sqsClient,
                sqsOptions,
                sp.GetRequiredService<ILogger<DeliveryEventDispatcher>>(),
                sp.GetRequiredService<DispatcherReadinessCheck>());
        });
    }

    private static void AddCloudApiClient(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CloudApiOrderServiceOptions>(configuration.GetSection("CloudApiOrderService"));
        services.AddTransient<TraceparentPropagationHandler>();
        services.AddHttpClient<ICloudApiOrderServiceClient, CloudApiOrderServiceClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<CloudApiOrderServiceOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        })
        .AddHttpMessageHandler<TraceparentPropagationHandler>();
    }

    private static void AddInfrastructure(IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddOpenApi();
    }

}

[ExcludeFromCodeCoverage]
internal static class ServiceDetails
{
    public static string Namespace { get; } = "PJIDeliveryEventService";

    public static string Name { get; } = "PJI.DeliveryEventService";

    public static string InstanceId { get; } = Environment.MachineName;
}