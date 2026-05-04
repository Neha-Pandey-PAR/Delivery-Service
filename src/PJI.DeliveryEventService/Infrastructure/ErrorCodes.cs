using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Infrastructure;

[ExcludeFromCodeCoverage]
internal static class ErrorCodes
{
    internal const string LocationNotFound = "LocationNotFound";
    internal const string ValidationError = "ValidationError";
    internal const string ServiceUnavailable = "ServiceUnavailable";
}
