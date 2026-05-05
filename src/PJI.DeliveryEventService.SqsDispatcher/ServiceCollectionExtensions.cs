using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.SqsDispatcher.CloudApiClient;
using PJI.DeliveryEventService.SqsDispatcher.Configuration;
using PJI.DeliveryEventService.SqsDispatcher.Dispatcher;
using PJI.DeliveryEventService.SqsDispatcher.Dispatcher.Processors;
using PJI.DeliveryEventService.SqsDispatcher.Infrastructure;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.SqsDispatcher;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers services required by the SQS-triggered Lambda function.
    /// This is a minimal set of services focused on processing delivery
    /// event messages and calling the Cloud API.
    /// </summary>
    public static void AddSqsDispatcherServices(this IServiceCollection services, IConfiguration configuration)
    {
        AddSerilog(services, configuration);
        AddConfiguration(services, configuration);
        AddProcessors(services);
        AddCloudApiClient(services, configuration);
    }

    private static void AddSerilog(IServiceCollection services, IConfiguration configuration)
    {
        var logLevel = configuration.GetValue("Serilog:MinimumLogLevel", LogEventLevel.Information);

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();

            var consoleLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service.name", "pji-delivery-event-service-sqs-dispatcher")
                .Enrich.WithProperty("service.version", configuration.GetValue<string>("Version") ?? "1.0.0")
                .MinimumLevel.Is(logLevel)
                .WriteTo.Console(new RenderedCompactJsonFormatter(), restrictedToMinimumLevel: logLevel)
                .CreateLogger();

            loggingBuilder.AddSerilog(consoleLogger);
        });
    }

    private static void AddConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        // Configuration options needed by processors
        services.Configure<HmacClientsOptions>(configuration.GetSection(HmacClientsOptions.SectionName));
        services.Configure<LocationOptions>(configuration.GetSection(LocationOptions.SectionName));
        services.Configure<CloudApiOrderServiceOptions>(configuration.GetSection("CloudApiOrderService"));
    }

    private static void AddProcessors(IServiceCollection services)
    {
        // Event processors - add new processors here as event types are added
        services.AddScoped<IDeliveryEventProcessor, DroppedOffEventProcessor>();

        // SQS message handler that routes messages to processors
        services.AddScoped<ISqsMessageHandler, SqsMessageHandler>();
    }

    private static void AddCloudApiClient(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<TraceparentPropagationHandler>();
        services.AddHttpClient<ICloudApiOrderServiceClient, CloudApiOrderServiceClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<CloudApiOrderServiceOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        })
        .AddHttpMessageHandler<TraceparentPropagationHandler>();
    }
}
