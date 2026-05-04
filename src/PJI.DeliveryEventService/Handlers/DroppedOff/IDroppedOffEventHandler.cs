using PJI.DeliveryEventService.Handlers.DroppedOff.Models;

namespace PJI.DeliveryEventService.Handlers.DroppedOff;

public interface IDroppedOffEventHandler
{
    Task<DroppedOffEventReply> HandleAsync(DroppedOffEventRequest request, CancellationToken cancellationToken);
}
