using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Configuration;

[ExcludeFromCodeCoverage]
public class Location
{
    public int LocationNumber { get; set; }
    public Guid LocationId { get; set; }
    public string LocationToken { get; set; } = string.Empty;
}
