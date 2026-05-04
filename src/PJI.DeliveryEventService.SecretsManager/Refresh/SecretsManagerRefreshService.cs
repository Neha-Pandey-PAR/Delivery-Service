using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;

namespace PJI.DeliveryEventService.SecretsManager.Refresh;

public class SecretsManagerRefreshService : BackgroundService
{
    private readonly ILogger<SecretsManagerRefreshService> _logger;
    private readonly SecretsManagerRefreshOptions _options;
    private readonly ISecretsManagerRefreshHandler _refreshHandler;
    private readonly IAmazonSecretsManager _secretsManager;
    private readonly IConfiguration _configuration;

    public SecretsManagerRefreshService(
        IOptions<SecretsManagerRefreshOptions> options,
        ISecretsManagerRefreshHandler refreshHandler,
        IAmazonSecretsManager secretsManager,
        IConfiguration configuration,
        ILogger<SecretsManagerRefreshService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _refreshHandler = refreshHandler ?? throw new ArgumentNullException(nameof(refreshHandler));
        _secretsManager = secretsManager ?? throw new ArgumentNullException(nameof(secretsManager));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = _options.RefreshInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);

                var startTime = DateTimeOffset.UtcNow;

                var secrets = await GetSecretsAsync(stoppingToken);

                await InvokeRefreshHandlerAsync(secrets);

                delay = _options.RefreshInterval - (DateTimeOffset.UtcNow - startTime);
            }
            catch (Exception e) when (e is not TaskCanceledException)
            {
                _logger.LogCritical(e, "Error performing secret manager tasks.");
            }
        }
    }

    internal async Task<IDictionary<string, string>> GetSecretsAsync(CancellationToken cancellationToken)
    {
        var secrets = new ConcurrentDictionary<string, string>();

        var secretIds = _configuration.GetSecretIds(rotationalSecretsOnly: true, startupSecretsOnly: false);

        if (secretIds.Count == 0)
            return secrets;

        var getSecretsBlock = new ActionBlock<KeyValuePair<string, string>>(
            async (pair) =>
            {
                var secretValue = await GetSecretValueAsync(pair.Value, cancellationToken);

                if (secretValue != null)
                {
                    secrets[pair.Key] = secretValue;
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

    internal async Task<string?> GetSecretValueAsync(string secretId, CancellationToken cancellationToken)
    {
        var request = new GetSecretValueRequest { SecretId = secretId };

        try
        {
            var response = await _secretsManager.GetSecretValueAsync(request, cancellationToken);
            return response.SecretString;
        }
        catch (Exception e) when (e is not TaskCanceledException)
        {
            _logger.LogCritical(e, "Error retrieving secret ID {SecretId}.", secretId);
            return null;
        }
    }

    internal async Task InvokeRefreshHandlerAsync(IDictionary<string, string> secrets)
    {
        if (secrets.Count > 0)
        {
            try
            {
                await _refreshHandler.RefreshSecretsAsync(secrets);
                _logger.LogInformation("Secrets refreshed successfully.");
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Error invoking secret refresh delegate.");
            }
        }
    }
}
