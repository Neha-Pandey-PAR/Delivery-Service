using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PJI.DeliveryEventService.Messaging.Models;

[ExcludeFromCodeCoverage]
public class DeliveryEventMessage
{
    public string EventType { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public int LocationNumber { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public JsonElement Payload { get; set; }
}
