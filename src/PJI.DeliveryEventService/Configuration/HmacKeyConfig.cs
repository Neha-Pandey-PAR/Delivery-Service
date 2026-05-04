using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Configuration;

[ExcludeFromCodeCoverage]
public class HmacKeyConfig
{
    public string KeyId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
