using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.HealthChecks;

[ExcludeFromCodeCoverage]
internal static class Tags
{
    internal const string Health = "health";
    internal const string Readiness = "readiness";
}
