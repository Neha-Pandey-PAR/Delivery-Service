using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Handlers.DroppedOff.Models;
using PJI.DeliveryEventService.Infrastructure;
using PJI.DeliveryEventService.Messaging;
using PJI.DeliveryEventService.Messaging.Models;
using System.Text.Json;

namespace PJI.DeliveryEventService.Handlers.DroppedOff;

public class DroppedOffEventHandler(
    IDeliveryEventQueue queue,
    IOptions<LocationOptions> locationOptions,
    ILogger<DroppedOffEventHandler> logger)
    : IDroppedOffEventHandler
{
    public async Task<DroppedOffEventReply> HandleAsync(DroppedOffEventRequest request, CancellationToken cancellationToken)
    {
        var location = locationOptions.Value.Items
            .FirstOrDefault(l => l.LocationNumber == request.LocationNumber);

        if (location is null)
            return new DroppedOffEventReply { ErrorCode = ErrorCodes.LocationNotFound };

        if (request.IsInternal)
        {
            logger.LogInformation(
                "1PD dropped-off received for orderId={OrderId}, locationNumber={LocationNumber}; no action required",
                request.OrderId, request.LocationNumber);
            return new DroppedOffEventReply { Success = true };
        }

        var message = new DeliveryEventMessage
        {
            EventType = DeliveryEventType.DroppedOff,
            EventId = request.EventId,
            LocationNumber = request.LocationNumber,
            ClientName = request.ClientName,
            CorrelationId = request.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new DroppedOffPayload
            {
                OrderId = request.OrderId,
                IsInternal = false,
                EmployeeId = request.EmployeeId
            })
        };

        await queue.EnqueueAsync(
            message,
            messageGroupId: request.OrderId.ToString(),
            deduplicationId: request.EventId,
            cancellationToken);

        return new DroppedOffEventReply { Success = true };
    }
}
