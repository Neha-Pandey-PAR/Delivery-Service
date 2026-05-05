using Amazon.Lambda.SQSEvents;
using PJI.DeliveryEventService.SqsDispatcher.Dispatcher;

namespace PJI.DeliveryEventService.SqsDispatcher;

/// <summary>
/// Handles individual SQS messages received by the Lambda function.
/// </summary>
public interface ISqsMessageHandler
{
    /// <summary>
    /// Processes a single SQS message by routing it to the appropriate
    /// <see cref="IDeliveryEventProcessor"/> based on event type.
    /// </summary>
    /// <param name="record">The SQS message record to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The processing result indicating success, failure type, or if the
    /// message should be discarded.
    /// </returns>
    Task<ProcessResult> HandleMessageAsync(SQSEvent.SQSMessage record, CancellationToken cancellationToken);
}
