using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Handlers.DroppedOff.Models;

[ExcludeFromCodeCoverage]
public class DroppedOffPayload
{
    public long OrderId { get; set; }
    public bool IsInternal { get; set; }
    public int? EmployeeId { get; set; }
}
