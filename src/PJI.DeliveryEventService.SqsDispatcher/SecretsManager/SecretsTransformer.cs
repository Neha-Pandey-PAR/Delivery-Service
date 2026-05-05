using System.Text.Json;

namespace PJI.DeliveryEventService.SqsDispatcher.SecretsManager;

internal static class SecretsTransformer
{
    internal static IEnumerable<KeyValuePair<string, string?>> Transform(string configurationKey, string secretValue)
    {
        if (configurationKey != Constants.HmacClientsSecretKey)
            yield break;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(secretValue);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            yield break;
        }

        if (!root.TryGetProperty("Clients", out var clients))
            yield break;

        foreach (var clientProp in clients.EnumerateObject())
        {
            var clientName = clientProp.Name;

            if (clientProp.Value.TryGetProperty("AccessToken", out var accessToken))
                yield return new KeyValuePair<string, string?>(
                    $"HmacClients:Clients:{clientName}:AccessToken",
                    accessToken.GetString());

            if (!clientProp.Value.TryGetProperty("Keys", out var keys))
                continue;

            var idx = 0;
            foreach (var key in keys.EnumerateArray())
            {
                if (key.TryGetProperty("KeyId", out var keyId))
                    yield return new KeyValuePair<string, string?>(
                        $"HmacClients:Clients:{clientName}:Keys:{idx}:KeyId",
                        keyId.GetString());

                if (key.TryGetProperty("Secret", out var secret))
                    yield return new KeyValuePair<string, string?>(
                        $"HmacClients:Clients:{clientName}:Keys:{idx}:Secret",
                        secret.GetString());

                if (key.TryGetProperty("IsActive", out var isActive))
                    yield return new KeyValuePair<string, string?>(
                        $"HmacClients:Clients:{clientName}:Keys:{idx}:IsActive",
                        isActive.GetBoolean().ToString());

                idx++;
            }
        }
    }
}
