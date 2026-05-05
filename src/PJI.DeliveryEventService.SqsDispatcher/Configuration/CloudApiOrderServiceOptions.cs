using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.SqsDispatcher.Configuration;

[ExcludeFromCodeCoverage]
public class CloudApiOrderServiceOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; }
}
