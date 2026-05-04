using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.SecretsManager.Refresh;
using System.Text.Json;

namespace PJI.DeliveryEventService.SecretsManager;

internal class SecretsManagerRefreshHandler(
    IOptionsMonitorCache<HmacClientsOptions> hmacClientsCache,
    ILogger<SecretsManagerRefreshHandler> logger)
    : ISecretsManagerRefreshHandler
{
    public Task RefreshSecretsAsync(IDictionary<string, string> secrets)
    {
        if (!secrets.TryGetValue(Constants.HmacClientsSecretKey, out var secretValue))
            return Task.CompletedTask;

        var transformed = SecretsTransformer.Transform(Constants.HmacClientsSecretKey, secretValue)
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);

        if (transformed.Count > 0)
        {
            hmacClientsCache.TryRemove(Options.DefaultName);
            logger.LogInformation("HMAC clients options refreshed from Secrets Manager");
        }

        return Task.CompletedTask;
    }
}
