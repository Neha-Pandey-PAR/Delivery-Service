using PJI.DeliveryEventService.CloudApiClient.Models;

namespace PJI.DeliveryEventService.CloudApiClient;

public interface ICloudApiOrderServiceClient
{
    Task<CloseOrderResult> CloseOrderAsync(
        long orderId, string accessToken, string locationToken,
        CancellationToken cancellationToken);
}
