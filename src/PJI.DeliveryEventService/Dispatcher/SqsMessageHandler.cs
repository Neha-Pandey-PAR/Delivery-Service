using Amazon.Lambda.SQSEvents;
using PJI.DeliveryEventService.Messaging.Models;
using Serilog.Context;
using System.Diagnostics;
using System.Text.Json;

namespace PJI.DeliveryEventService.Dispatcher;

/// <summary>
/// Handles SQS messages received by the Lambda function by deserializing them
/// and routing to the appropriate <see cref="IDeliveryEventProcessor"/>.
/// </summary>
/// <remarks>
/// This handler is the Lambda equivalent of <see cref="DeliveryEventDispatcher"/>'s
/// message processing logic. The key difference is that Lambda manages message
/// visibility and deletion, so this handler only needs to return the processing
/// result - it doesn't handle retries or DLQ routing directly.
/// </remarks>
public class SqsMessageHandler(
    IEnumerable<IDeliveryEventProcessor> processors,
    ILogger<SqsMessageHandler> logger)
    : ISqsMessageHandler
{
    private readonly Dictionary<string, IDeliveryEventProcessor> _processors =
        processors.ToDictionary(p => p.EventType);

    /// <inheritdoc />
    public async Task<ProcessResult> HandleMessageAsync(
        SQSEvent.SQSMessage record,
        CancellationToken cancellationToken)
    {
        DeliveryEventMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<DeliveryEventMessage>(record.Body);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "Failed to deserialize SQS message body for messageId={MessageId}; discarding. Body={Body}",
                record.MessageId, record.Body);
            return ProcessResult.Discard;
        }

        if (message is null)
        {
            logger.LogError(
                "Deserialized SQS message body was null for messageId={MessageId}; discarding",
                record.MessageId);
            return ProcessResult.Discard;
        }

        if (!_processors.TryGetValue(message.EventType, out var processor))
        {
            logger.LogWarning(
                "No processor registered for event type {EventType} on messageId={MessageId}; discarding",
                message.EventType, record.MessageId);
            return ProcessResult.Discard;
        }

        // Restore the original W3C trace context so outgoing HTTP calls (e.g. Cloud API)
        // carry the same trace ID that was active during the inbound HTTP request that
        // enqueued this message.
        var parentTraceId = ActivityTraceId.CreateFromString(message.CorrelationId.ToString("N").AsSpan());
        using var processingActivity = new Activity("sqs.process")
            .SetParentId(parentTraceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded)
            .Start();

        // Push the message's CorrelationId into LogContext so every log emitted during
        // processing is enriched with the same correlation id used for the inbound HTTP request.
        using (LogContext.PushProperty("CorrelationId", message.CorrelationId))
        {
            logger.LogInformation(
                "Processing message messageId={MessageId} eventType={EventType} eventId={EventId}",
                record.MessageId, message.EventType, message.EventId);

            try
            {
                var result = await processor.ProcessAsync(message, cancellationToken);

                switch (result)
                {
                    case ProcessResult.Success:
                        logger.LogInformation(
                            "Successfully processed messageId={MessageId} eventType={EventType}",
                            record.MessageId, message.EventType);
                        break;

                    case ProcessResult.Discard:
                        logger.LogWarning(
                            "Discarding messageId={MessageId} eventType={EventType} due to processor decision",
                            record.MessageId, message.EventType);
                        break;

                    case ProcessResult.NonRetriable:
                        logger.LogError(
                            "Non-retriable failure for messageId={MessageId} eventType={EventType}; message will be deleted and may go to DLQ after max receives",
                            record.MessageId, message.EventType);
                        break;

                    case ProcessResult.Retriable:
                        logger.LogWarning(
                            "Retriable failure for messageId={MessageId} eventType={EventType}; returning for retry",
                            record.MessageId, message.EventType);
                        break;
                }

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Processing cancelled for messageId={MessageId} eventType={EventType}; returning for retry",
                    record.MessageId, message.EventType);
                return ProcessResult.Retriable;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Unexpected error processing messageId={MessageId} eventType={EventType}; returning for retry",
                    record.MessageId, message.EventType);
                return ProcessResult.Retriable;
            }
        }
    }
}
