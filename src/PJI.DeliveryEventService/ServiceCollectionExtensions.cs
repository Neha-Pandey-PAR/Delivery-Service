using Amazon.SQS;
using Asp.Versioning;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PJI.DeliveryEventService.Authentication;
using PJI.DeliveryEventService.Configuration;
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
    /// <summary>
    /// Registers all application services. Invoked from both the local
    /// development host (<see cref="Program"/>) and the Lambda hosting
    /// model (<see cref="LambdaEntryPoint"/>) via <see cref="Startup"/>.
    /// </summary>
    internal static void AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        AddSerilog(services, configuration);

        AddSecretsRefresh(services, configuration);
        AddAwsServices(services, configuration);
        AddHmacAuthentication(services, configuration);
        AddApiVersioningAndControllers(services);
        AddHealthChecks(services);
        AddHandlers(services, configuration);
        AddMessaging(services, configuration);
        AddMessagingInfrastructure(services);
        AddInfrastructure(services);
    }

    private static void AddSerilog(IServiceCollection services, IConfiguration configuration)
    {
        var logLevel = configuration.GetValue<LogEventLevel>("Serilog:MinimumLogLevel");

        // Lambda's filesystem is ephemeral - the File sink is only useful for
        // local/container hosting. Detect Lambda via the runtime-injected env
        // var and fall back to console-only logging when present.
        var isLambda = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME"));
        var fileLogPath = configuration.GetValue<string>("Serilog:PathAndFileNameBase");
        var enableFileLog = !isLambda && !string.IsNullOrEmpty(fileLogPath);

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();

            if (enableFileLog)
            {
                var fileLogger = SerilogLoggerConfig(configuration, allowOverrides: true)
                    .MinimumLevel.Is(logLevel)
                    .WriteTo.File(
                        new RenderedCompactJsonFormatter(),
                        path: fileLogPath!,
                        restrictedToMinimumLevel: logLevel,
                        fileSizeLimitBytes: configuration.GetValue<long>("Serilog:FileSizeLimitBytes"),
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: configuration.GetValue<int>("Serilog:RetainedFileLimitCount"))
                    .CreateLogger();

                loggingBuilder.AddSerilog(fileLogger);
            }

            if (configuration.GetValue<bool>("Serilog:EnableConsoleLog") || isLambda)
            {
                var consoleLogLevel = configuration.GetValue<LogEventLevel>("Serilog:MinimumLogLevelForConsole");

                var consoleLogger = SerilogLoggerConfig(configuration, allowOverrides: false)
                    .MinimumLevel.Is(consoleLogLevel)
                    .WriteTo.Console(
                        new RenderedCompactJsonFormatter(),
                        restrictedToMinimumLevel: consoleLogLevel)
                    .CreateLogger();

                loggingBuilder.AddSerilog(consoleLogger);
            }
        });
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
        services.AddHealthChecks()
            .AddCheck("liveness", () => HealthCheckResult.Healthy(), tags: [Tags.Health])
            .AddCheck("readiness", () => HealthCheckResult.Healthy(), tags: [Tags.Readiness]);
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

    private static void AddMessagingInfrastructure(IServiceCollection services)
    {
        // Resolves the SQS queue URL at startup. Required by DeliveryEventQueue
        // which sends messages to SQS. The actual message processing is now
        // handled by the separate SqsDispatcher Lambda triggered by SQS.
        services.AddHostedService<SqsQueueUrlResolver>();
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