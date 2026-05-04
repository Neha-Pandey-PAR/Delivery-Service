using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Configuration;

[ExcludeFromCodeCoverage]
public class HmacClientsOptions
{
    public const string SectionName = "HmacClients";
    public Dictionary<string, HmacClientConfig> Clients { get; set; } = new();
}
