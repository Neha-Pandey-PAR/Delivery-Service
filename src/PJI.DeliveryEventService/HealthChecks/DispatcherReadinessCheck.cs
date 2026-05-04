using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PJI.DeliveryEventService.HealthChecks;

public sealed class DispatcherReadinessCheck : IHealthCheck
{
    private volatile bool _isReady;

    internal void MarkReady() => _isReady = true;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(_isReady
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Dispatcher has not started."));
}
