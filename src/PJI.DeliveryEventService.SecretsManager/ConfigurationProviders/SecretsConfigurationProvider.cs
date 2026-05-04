using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;

namespace PJI.DeliveryEventService.SecretsManager.ConfigurationProviders;

public class SecretsConfigurationProvider : ConfigurationProvider
{
    private readonly IConfiguration _configuration;
    private readonly IAmazonSecretsManager _amazonSecretsManager;
    private readonly SecretValueTransformerDelegate _transformer;

    public SecretsConfigurationProvider(IConfiguration configuration, IAmazonSecretsManager amazonSecretsManager, SecretValueTransformerDelegate transformer)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _amazonSecretsManager = amazonSecretsManager ?? throw new ArgumentNullException(nameof(amazonSecretsManager));
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
    }

    public override void Load()
    {
        Data = GetSecretsAsync().GetAwaiter().GetResult();
    }

    private async Task<IDictionary<string, string?>> GetSecretsAsync()
    {
        var secrets = new ConcurrentDictionary<string, string?>();

        var secretIds = _configuration.GetSecretIds(rotationalSecretsOnly: false, startupSecretsOnly: true);

        if (secretIds.Count == 0)
            return secrets;

        var getSecretsBlock = new ActionBlock<KeyValuePair<string, string>>(
            async (pair) =>
            {
                var secretValue = await GetSecretValueAsync(pair);

                if (secretValue != null)
                {
                    foreach (var transformedValue in _transformer.Invoke(pair.Key, secretValue))
                    {
                        secrets[transformedValue.Key] = transformedValue.Value;
                    }
                }
            },
            new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = secretIds.Count }
        );

        foreach (var pair in secretIds)
        {
            getSecretsBlock.Post(pair);
        }

        getSecretsBlock.Complete();
        await getSecretsBlock.Completion;

        return secrets;
    }

    private async Task<string?> GetSecretValueAsync(KeyValuePair<string, string> pair)
    {
        var request = new GetSecretValueRequest { SecretId = pair.Value };

        try
        {
            var response = await _amazonSecretsManager.GetSecretValueAsync(request);
            return response.SecretString;
        }
        catch (ResourceNotFoundException e)
        {
            throw new KeyNotFoundException($"Secret '{pair.Value}' not found.", e);
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public delegate IEnumerable<KeyValuePair<string, string?>> SecretValueTransformerDelegate(string configurationKey, string secretValue);
}
