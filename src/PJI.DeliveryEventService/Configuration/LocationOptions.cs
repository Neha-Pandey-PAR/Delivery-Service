using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Configuration;

[ExcludeFromCodeCoverage]
public class LocationOptions
{
    public const string SectionName = "Locations";
    public List<Location> Items { get; set; } = [];
}
