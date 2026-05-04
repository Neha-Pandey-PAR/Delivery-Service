using PJI.DeliveryEventService.SecretsManager;
using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService;

/// <summary>
/// Local development entry point. The same <see cref="Startup"/> is used by
/// the Lambda hosting model (<see cref="LambdaEntryPoint"/>) so service
/// registration and the middleware pipeline stay in one place.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Application entry point.")]
public class Program
{
    public static Task Main(string[] args)
    {
        return CreateHostBuilder(args).Build().RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((ctx, config) =>
                config.AddSecretsManager(ctx.Configuration))
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseShutdownTimeout(TimeSpan.FromSeconds(30))
                    .UseStartup<Startup>();
            });
}
