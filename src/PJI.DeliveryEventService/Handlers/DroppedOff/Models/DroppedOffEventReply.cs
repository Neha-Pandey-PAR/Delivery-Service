using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Handlers.DroppedOff.Models;

[ExcludeFromCodeCoverage]
public class DroppedOffEventReply
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
}
