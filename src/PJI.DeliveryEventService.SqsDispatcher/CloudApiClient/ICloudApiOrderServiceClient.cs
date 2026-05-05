using PJI.DeliveryEventService.SqsDispatcher.CloudApiClient.Models;

namespace PJI.DeliveryEventService.SqsDispatcher.CloudApiClient;

public interface ICloudApiOrderServiceClient
{
    Task<CloseOrderResult> CloseOrderAsync(
        long orderId, string accessToken, string locationToken,
        CancellationToken cancellationToken);
}
