using Microsoft.Extensions.Configuration;

namespace PJI.DeliveryEventService.SecretsManager;

internal static class SecretsConfiguration
{
    private const string SecretsSectionName = "Secrets";

    internal static IDictionary<string, string> GetSecretIds(this IConfiguration configuration, bool rotationalSecretsOnly, bool startupSecretsOnly)
    {
        var dictionary = new Dictionary<string, string>();
        var secrets = configuration.GetSecrets(rotationalSecretsOnly, startupSecretsOnly);

        foreach (var child in secrets)
        {
            if (!string.IsNullOrWhiteSpace(child.Key))
            {
                dictionary.Add(child.Key, child.Value.Name);
            }
        }

        return dictionary;
    }

    private static IDictionary<string, Secret> GetSecrets(this IConfiguration configuration, bool rotationalSecretsOnly, bool startupSecretsOnly)
    {
        var secretsSection = configuration.GetSection(SecretsSectionName).Get<Dictionary<string, Secret>>();
        if (secretsSection != null)
        {
            var nonNullableSecrets = secretsSection.Where(x => !string.IsNullOrWhiteSpace(x.Value.Name));

            if (rotationalSecretsOnly)
                nonNullableSecrets = nonNullableSecrets.Where(x => x.Value.RotationEnabled);

            if (startupSecretsOnly)
                nonNullableSecrets = nonNullableSecrets.Where(x => x.Value.LoadOnStartup);

            return nonNullableSecrets.ToDictionary(x => x.Key, x => x.Value);
        }

        return new Dictionary<string, Secret>();
    }
}

internal class Secret
{
    public string Name { get; set; } = string.Empty;
    public bool RotationEnabled { get; set; }
    public bool LoadOnStartup { get; set; }
}
