using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.HealthChecks;
using PJI.DeliveryEventService.Messaging.Models;
using Serilog.Context;
using System.Diagnostics;
using System.Text.Json;

namespace PJI.DeliveryEventService.Dispatcher;

public class DeliveryEventDispatcher(
    IEnumerable<IDeliveryEventProcessor> processors,
    IAmazonSQS sqsClient,
    IOptions<SqsOptions> sqsOptions,
    ILogger<DeliveryEventDispatcher> logger,
    DispatcherReadinessCheck readinessCheck)
    : BackgroundService
{
    private readonly Dictionary<string, IDeliveryEventProcessor> _processors =
        processors.ToDictionary(p => p.EventType);
    private readonly string _queueUrl = sqsOptions.Value.PersistenceQueueUrl;
    private readonly int _visibilityTimeoutSeconds = sqsOptions.Value.VisibilityTimeoutSeconds;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        readinessCheck.MarkReady();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = 1,
                    WaitTimeSeconds = 20,
                    MessageAttributeNames = ["All"],
                    MessageSystemAttributeNames = ["ApproximateReceiveCount"]
                }, stoppingToken);

                if (response.Messages != null)
                {
                    foreach (var msg in response.Messages)
                    {
                        await OnMessageReceivedAsync(msg, stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error polling SQS from queue {QueueUrl}", _queueUrl);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task OnMessageReceivedAsync(Message sqsMessage, CancellationToken ct)
    {
        DeliveryEventMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<DeliveryEventMessage>(sqsMessage.Body);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "Failed to deserialize SQS message body for messageId={MessageId}; deleting message. Body={Body}",
                sqsMessage.MessageId, sqsMessage.Body);
            await DeleteMessageAsync(sqsMessage.ReceiptHandle);
            return;
        }

        if (message is null)
        {
            logger.LogError(
                "Deserialized SQS message body was null for messageId={MessageId}; deleting message",
                sqsMessage.MessageId);
            await DeleteMessageAsync(sqsMessage.ReceiptHandle);
            return;
        }

        if (!_processors.TryGetValue(message.EventType, out var processor))
        {
            logger.LogWarning(
                "No processor registered for event type {EventType} on messageId={MessageId}; deleting message",
                message.EventType, sqsMessage.MessageId);
            await DeleteMessageAsync(sqsMessage.ReceiptHandle);
            return;
        }

        using var renewalCts = new CancellationTokenSource();
        var renewalTask = StartVisibilityRenewalAsync(sqsMessage.ReceiptHandle, renewalCts.Token);

        // Restore the original W3C trace context so outgoing HTTP calls (e.g. Cloud API) carry
        // the same trace ID that was active during the inbound HTTP request that enqueued this message.
        var parentTraceId = ActivityTraceId.CreateFromString(message.CorrelationId.ToString("N").AsSpan());
        using var processingActivity = new Activity("sqs.process")
            .SetParentId(parentTraceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded)
            .Start();

        ProcessResult result;
        // Push the message's CorrelationId into LogContext so every log emitted during
        // processing — by the processor, the cloud-api client, and the dispatcher's own
        // routing log below — is enriched with the same correlation id used for the inbound HTTP request.
        using (LogContext.PushProperty("CorrelationId", message.CorrelationId))
        {
            try
            {
                result = await processor.ProcessAsync(message, ct);
            }
            finally
            {
                await renewalCts.CancelAsync();
                await renewalTask;
            }

            switch (result)
            {
                case ProcessResult.Success:
                case ProcessResult.Discard:
                    await DeleteMessageAsync(sqsMessage.ReceiptHandle);
                    return;

                case ProcessResult.NonRetriable:
                    logger.LogCritical(
                        "Non-retriable failure for messageId={MessageId} eventType={EventType}; fast-failing to DLQ",
                        sqsMessage.MessageId, message.EventType);
                    await sqsClient.ChangeMessageVisibilityAsync(_queueUrl, sqsMessage.ReceiptHandle, 0);
                    return;

                case ProcessResult.Retriable:
                    var receiveCount = int.Parse(sqsMessage.Attributes["ApproximateReceiveCount"]);
                    var delay = CalculateRetryVisibilityTimeout(receiveCount);
                    logger.LogWarning(
                        "Transient failure for messageId={MessageId} eventType={EventType}; retrying in {Delay}s (attempt {ReceiveCount})",
                        sqsMessage.MessageId, message.EventType, delay, receiveCount);
                    await sqsClient.ChangeMessageVisibilityAsync(_queueUrl, sqsMessage.ReceiptHandle, delay);
                    return;
            }
        }
    }

    // Visibility renewal: at (VisibilityTimeout - 45s), if the processor is still working,
    // extend the visibility by another full VisibilityTimeoutSeconds. The 45s buffer is
    // safety margin — it ensures the renewal API call lands BEFORE the original timeout
    // expires, even under network latency or SQS API slowness. Without this, a long-running
    // processor would lose the message back to the queue mid-flight, allowing another
    // consumer to pick it up while we're still working.
    private async Task StartVisibilityRenewalAsync(string receiptHandle, CancellationToken ct)
    {
        var renewalThreshold = _visibilityTimeoutSeconds - 45;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(renewalThreshold), ct);
            if (!ct.IsCancellationRequested)
            {
                await sqsClient.ChangeMessageVisibilityAsync(_queueUrl, receiptHandle, _visibilityTimeoutSeconds);
                logger.LogDebug("Renewed message visibility by {Seconds}s", _visibilityTimeoutSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            // Processing completed before renewal threshold — expected, no-op.
        }
    }

    // Exponential backoff with ±20% jitter, capped at 300s.
    // Examples (base = 30s, ratio = 2):
    //   receiveCount=1 → 30s  ± 6s  (range [24, 36])
    //   receiveCount=2 → 60s  ± 12s (range [48, 72])
    //   receiveCount=3 → 120s ± 24s (range [96, 144])
    //   receiveCount=4 → 240s ± 48s (range [192, 288])
    //   receiveCount=5+ → base capped at 300s, ±20% jitter applied after → [240, 360]
    // The jitter spreads retry storms across consumers; the cap prevents indefinite delay.
    internal static int CalculateRetryVisibilityTimeout(int receiveCount)
    {
        var delaySecs = Math.Min(300, 30 * (int)Math.Pow(2, receiveCount - 1));
        var jitter = Random.Shared.NextDouble() * 0.4 * delaySecs - 0.2 * delaySecs;
        return Math.Max(1, (int)(delaySecs + jitter));
    }

    private async Task DeleteMessageAsync(string receiptHandle)
    {
        await sqsClient.DeleteMessageAsync(_queueUrl, receiptHandle);
    }
}
