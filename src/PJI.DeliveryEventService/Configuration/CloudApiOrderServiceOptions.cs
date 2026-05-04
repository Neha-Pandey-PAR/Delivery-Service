using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Configuration;

[ExcludeFromCodeCoverage]
public class CloudApiOrderServiceOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; }
}
