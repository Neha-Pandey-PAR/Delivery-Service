using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PJI.DeliveryEventService.HealthChecks;
using PJI.DeliveryEventService.Infrastructure;
using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService;

/// <summary>
/// Classic Startup pattern used by the Lambda hosting model
/// (<see cref="LambdaEntryPoint"/>) and by local development hosting in
/// <see cref="Program"/>. Service registration is delegated to
/// <see cref="ServiceCollectionExtensions.AddServices"/> so the same DI graph
/// is used in both hosting modes.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Application bootstrap.")]
public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddServices(Configuration);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseRouting();

        app.UseExceptionHandler();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHealthChecks("/", new HealthCheckOptions
            {
                Predicate = c => c.Tags.Contains(Tags.Health)
            });
            endpoints.MapHealthChecks("/health", new HealthCheckOptions
            {
                Predicate = c => c.Tags.Contains(Tags.Health)
            });
            endpoints.MapHealthChecks("/readiness", new HealthCheckOptions
            {
                Predicate = c => c.Tags.Contains(Tags.Readiness)
            });

            if (Configuration.GetValue<bool>("EnableDocs"))
            {
                endpoints.MapOpenApi();
            }

            endpoints.MapControllers();
        });
    }
}
