using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using PJI.DeliveryEventService.SecretsManager.ConfigurationProviders;
using System.Diagnostics.CodeAnalysis;
using static PJI.DeliveryEventService.SecretsManager.ConfigurationProviders.SecretsConfigurationProvider;

namespace PJI.DeliveryEventService.SecretsManager.ConfigurationSources;

[ExcludeFromCodeCoverage(Justification = "Configuration class.")]
public class SecretsConfigurationSource : IConfigurationSource
{
    private readonly IConfiguration _configuration;
    private readonly SecretValueTransformerDelegate _transformer;

    public SecretsConfigurationSource(IConfiguration configuration, SecretValueTransformerDelegate transformer)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SecretsConfigurationProvider(_configuration, GetSecretsManager(_configuration), _transformer);
    }

    private IAmazonSecretsManager GetSecretsManager(IConfiguration configuration)
    {
        var section = configuration.GetSection("AWS");

        if (!section.Exists())
        {
            return new AmazonSecretsManagerClient();
        }

        RegionEndpoint? region = null;
        var regionSystemName = section["Region"];

        if (!string.IsNullOrWhiteSpace(regionSystemName))
        {
            region = RegionEndpoint.GetBySystemName(regionSystemName);
        }

        AWSCredentials? credentials = null;
        var profileName = section["Profile"];

        if (!string.IsNullOrWhiteSpace(profileName))
        {
            _ = new CredentialProfileStoreChain().TryGetAWSCredentials(profileName, out credentials);
        }

        return (region, credentials) switch
        {
            (null, null) => new AmazonSecretsManagerClient(),
            (null, not null) => new AmazonSecretsManagerClient(credentials),
            (not null, null) => new AmazonSecretsManagerClient(region),
            (not null, not null) => new AmazonSecretsManagerClient(credentials, region),
        };
    }
}
