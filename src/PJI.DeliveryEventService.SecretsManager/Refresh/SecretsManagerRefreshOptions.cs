using Microsoft.Extensions.Options;

namespace PJI.DeliveryEventService.SecretsManager.Refresh;

public class SecretsManagerRefreshOptions
{
    public TimeSpan RefreshInterval { get; set; }

    public void Validate()
    {
        if (RefreshInterval <= TimeSpan.Zero)
        {
            throw new OptionsValidationException(string.Empty,
                typeof(SecretsManagerRefreshOptions),
                ["Refresh interval must be greater than 00:00:00."]);
        }
    }
}
