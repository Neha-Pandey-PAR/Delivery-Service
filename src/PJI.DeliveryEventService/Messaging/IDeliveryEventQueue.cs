using PJI.DeliveryEventService.Messaging.Models;

namespace PJI.DeliveryEventService.Messaging;

public interface IDeliveryEventQueue
{
    Task EnqueueAsync(DeliveryEventMessage message, string messageGroupId,
        string deduplicationId, CancellationToken cancellationToken);
}
