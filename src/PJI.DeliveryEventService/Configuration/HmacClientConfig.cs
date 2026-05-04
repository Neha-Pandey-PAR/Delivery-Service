using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Configuration;

[ExcludeFromCodeCoverage]
public class HmacClientConfig
{
    public string AccessToken { get; set; } = string.Empty;
    public List<HmacKeyConfig> Keys { get; set; } = [];
}
