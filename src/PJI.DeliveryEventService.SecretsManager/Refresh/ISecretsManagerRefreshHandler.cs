namespace PJI.DeliveryEventService.SecretsManager.Refresh;

public interface ISecretsManagerRefreshHandler
{
    Task RefreshSecretsAsync(IDictionary<string, string> secrets);
}
