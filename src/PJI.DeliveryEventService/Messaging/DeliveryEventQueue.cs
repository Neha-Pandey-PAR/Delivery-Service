using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Messaging.Models;
using System.Text.Json;

namespace PJI.DeliveryEventService.Messaging;

public class DeliveryEventQueue(
    IAmazonSQS sqsClient,
    IOptions<SqsOptions> options,
    ILogger<DeliveryEventQueue> logger)
    : IDeliveryEventQueue
{
    private readonly string _queueUrl = options.Value.PersistenceQueueUrl;

    public async Task EnqueueAsync(DeliveryEventMessage message, string messageGroupId,
        string deduplicationId, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Enqueueing {EventType} event for locationNumber={LocationNumber}, eventId={EventId}, deduplicationId={DeduplicationId}",
            message.EventType, message.LocationNumber, message.EventId, deduplicationId);

        await sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = JsonSerializer.Serialize(message),
            MessageGroupId = messageGroupId,
            MessageDeduplicationId = deduplicationId
        }, cancellationToken);
    }
}
