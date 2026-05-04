using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PJI.DeliveryEventService;
using PJI.DeliveryEventService.HealthChecks;
using PJI.DeliveryEventService.Infrastructure;
using System.Diagnostics.CodeAnalysis;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(30));
builder.AddServices();

var app = builder.Build();

app.UseRouting();
app.MapHealthChecks("/", new HealthCheckOptions { Predicate = c => c.Tags.Contains(Tags.Health) });
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => c.Tags.Contains(Tags.Health) });
app.MapHealthChecks("/readiness", new HealthCheckOptions { Predicate = c => c.Tags.Contains(Tags.Readiness) });

if (app.Configuration.GetValue<bool>("EnableDocs"))
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

[ExcludeFromCodeCoverage(Justification = "Application entry point.")]
public partial class Program { }
