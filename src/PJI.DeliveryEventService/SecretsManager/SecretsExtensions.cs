using PJI.DeliveryEventService.SecretsManager.ConfigurationSources;
using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.SecretsManager;

[ExcludeFromCodeCoverage(Justification = "Thin DI wrapper; covered by integration.")]
internal static class SecretsExtensions
{
    internal static IConfigurationBuilder AddSecretsManager(this IConfigurationBuilder builder, IConfiguration configuration)
    {
        return builder.Add(new SecretsConfigurationSource(configuration, SecretsTransformer.Transform));
    }
}
