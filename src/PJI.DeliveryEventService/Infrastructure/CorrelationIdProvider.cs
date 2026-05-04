using System.Diagnostics;

namespace PJI.DeliveryEventService.Infrastructure;

internal static class CorrelationIdProvider
{
    internal static Guid GetOrCreate()
    {
        var traceId = Activity.Current?.TraceId.ToString();
        return traceId is { Length: 32 } ? Guid.Parse(traceId) : Guid.CreateVersion7();
    }
}
