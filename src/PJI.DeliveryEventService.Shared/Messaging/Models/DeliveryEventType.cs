using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Messaging.Models;

[ExcludeFromCodeCoverage]
public static class DeliveryEventType
{
    public const string DroppedOff = "dropped-off";
    public const string CheckedIn = "checked-in";
    public const string CheckedOut = "checked-out";
}
