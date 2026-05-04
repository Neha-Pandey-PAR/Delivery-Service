using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Messaging;
using PJI.DeliveryEventService.Messaging.Models;
using System.Text.Json;

namespace PJI.DeliveryEventService.Tests.Messaging;

public class DeliveryEventQueueTests
{
    private const string QueueUrl = "https://sqs/test-queue.fifo";
    private readonly Mock<IAmazonSQS> _sqsMock = new(MockBehavior.Strict);
    private readonly SqsOptions _sqsOptions = new()
    {
        PersistenceQueueUrl = QueueUrl,
        VisibilityTimeoutSeconds = 120
    };

    private DeliveryEventQueue CreateQueue() =>
        new(_sqsMock.Object, Options.Create(_sqsOptions), NullLogger<DeliveryEventQueue>.Instance);

    private static DeliveryEventMessage CreateMessage() => new()
    {
        EventType = DeliveryEventType.DroppedOff,
        EventId = "evt-1",
        LocationNumber = 12345,
        ClientName = "PJI",
        CorrelationId = Guid.NewGuid(),
        Payload = JsonSerializer.SerializeToElement(new { OrderId = 999L })
    };

    [Fact]
    public async Task EnqueueAsync_ShouldSendMessageToConfiguredQueueUrl()
    {
        // Arrange
        SendMessageRequest? captured = null;
        _sqsMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new SendMessageResponse());
        var queue = CreateQueue();

        // Act
        await queue.EnqueueAsync(CreateMessage(), "group-1", "dedup-1", CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.QueueUrl.Should().Be(QueueUrl);
        _sqsMock.VerifyAll();
        _sqsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnqueueAsync_ShouldSerializeMessageAsBody()
    {
        // Arrange
        var message = CreateMessage();
        SendMessageRequest? captured = null;
        _sqsMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new SendMessageResponse());
        var queue = CreateQueue();

        // Act
        await queue.EnqueueAsync(message, "group-1", "dedup-1", CancellationToken.None);

        // Assert
        var deserialized = JsonSerializer.Deserialize<DeliveryEventMessage>(captured!.MessageBody);
        deserialized.Should().NotBeNull();
        deserialized!.EventType.Should().Be(message.EventType);
        deserialized.EventId.Should().Be(message.EventId);
        deserialized.LocationNumber.Should().Be(message.LocationNumber);
        _sqsMock.VerifyAll();
        _sqsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnqueueAsync_ShouldPropagateGroupIdAndDeduplicationId()
    {
        // Arrange
        SendMessageRequest? captured = null;
        _sqsMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new SendMessageResponse());
        var queue = CreateQueue();

        // Act
        await queue.EnqueueAsync(CreateMessage(), "order-555", "evt-555", CancellationToken.None);

        // Assert
        captured!.MessageGroupId.Should().Be("order-555");
        captured.MessageDeduplicationId.Should().Be("evt-555");
        _sqsMock.VerifyAll();
        _sqsMock.VerifyNoOtherCalls();
    }
}
