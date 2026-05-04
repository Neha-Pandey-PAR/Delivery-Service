using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Handlers.DroppedOff.Models;

[ExcludeFromCodeCoverage]
public class DroppedOffEventRequest
{
    public long OrderId { get; init; }
    public string EventId { get; init; } = string.Empty;
    public int LocationNumber { get; init; }
    public bool IsInternal { get; init; }
    public int? EmployeeId { get; init; }
    public string ClientName { get; init; } = string.Empty;
    public Guid CorrelationId { get; init; }
}
