using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Messaging.Models;
using PJI.DeliveryEventService.Models;
using PJI.DeliveryEventService.SqsDispatcher.CloudApiClient;
using PJI.DeliveryEventService.SqsDispatcher.CloudApiClient.Models;
using System.Text.Json;

namespace PJI.DeliveryEventService.SqsDispatcher.Dispatcher.Processors;

public class DroppedOffEventProcessor(
    ICloudApiOrderServiceClient cloudApiClient,
    IOptionsMonitor<HmacClientsOptions> hmacClientsOptions,
    IOptionsMonitor<LocationOptions> locationOptions,
    ILogger<DroppedOffEventProcessor> logger)
    : IDeliveryEventProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string EventType => DeliveryEventType.DroppedOff;

    public async Task<ProcessResult> ProcessAsync(DeliveryEventMessage message, CancellationToken cancellationToken)
    {
        DroppedOffPayload? payload;
        try
        {
            payload = message.Payload.Deserialize<DroppedOffPayload>(JsonOptions);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            logger.LogCritical(
                "Cannot deserialize DroppedOff payload for eventId={EventId}; discarding message",
                message.EventId);
            return ProcessResult.Discard;
        }

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OrderId"] = payload.OrderId,
            ["EventId"] = message.EventId,
            ["EventType"] = message.EventType,
            ["LocationNumber"] = message.LocationNumber
        });

        logger.LogInformation(
            "Processing {EventType} event for orderId={OrderId}, eventId={EventId}",
            message.EventType, payload.OrderId, message.EventId);

        if (!hmacClientsOptions.CurrentValue.Clients.TryGetValue(message.ClientName, out var clientConfig))
        {
            logger.LogCritical(
                "Cannot resolve access token for client {ClientName}; discarding message",
                message.ClientName);
            return ProcessResult.Discard;
        }

        var location = locationOptions.CurrentValue.Items
            .FirstOrDefault(l => l.LocationNumber == message.LocationNumber);

        if (location is null)
        {
            logger.LogCritical(
                "Cannot resolve location token for locationNumber {LocationNumber}; discarding message",
                message.LocationNumber);
            return ProcessResult.Discard;
        }

        var result = await cloudApiClient.CloseOrderAsync(
            payload.OrderId, clientConfig.AccessToken, location.LocationToken,
            cancellationToken);

        // CloudApiOrderServiceClient already logs status code + response body on non-success.
        // Only log here on success (unique info) — failure routing (DLQ vs retry) is logged by the dispatcher.
        if (result == CloseOrderResult.Success)
        {
            logger.LogInformation("Order {OrderId} closed successfully", payload.OrderId);
            return ProcessResult.Success;
        }

        return result == CloseOrderResult.NonRetriable
            ? ProcessResult.NonRetriable
            : ProcessResult.Retriable;
    }
}
