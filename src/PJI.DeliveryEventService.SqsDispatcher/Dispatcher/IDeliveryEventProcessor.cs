using PJI.DeliveryEventService.Messaging.Models;

namespace PJI.DeliveryEventService.SqsDispatcher.Dispatcher;

public interface IDeliveryEventProcessor
{
    string EventType { get; }
    Task<ProcessResult> ProcessAsync(DeliveryEventMessage message, CancellationToken cancellationToken);
}

public enum ProcessResult
{
    Success,
    Retriable,
    NonRetriable,
    Discard
}
