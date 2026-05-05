using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.SqsDispatcher.SecretsManager;

[ExcludeFromCodeCoverage]
internal static class Constants
{
    internal const string HmacClientsSecretKey = "HmacClients";
}
