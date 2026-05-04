using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Dispatcher;
using PJI.DeliveryEventService.HealthChecks;
using PJI.DeliveryEventService.Messaging.Models;
using System.Text.Json;

namespace PJI.DeliveryEventService.Tests.Dispatcher;

public class DeliveryEventDispatcherTests
{
    private const string QueueUrl = "https://sqs/test-queue";
    private readonly Mock<IAmazonSQS> _sqsMock = new(MockBehavior.Strict);
    private readonly Mock<IDeliveryEventProcessor> _processorMock = new(MockBehavior.Strict);
    private readonly SqsOptions _sqsOptions = new()
    {
        PersistenceQueueUrl = QueueUrl,
        VisibilityTimeoutSeconds = 120
    };

    public DeliveryEventDispatcherTests()
    {
        _processorMock.SetupGet(p => p.EventType).Returns(DeliveryEventType.DroppedOff);
    }

    private DeliveryEventDispatcher CreateDispatcher() =>
        new([_processorMock.Object], _sqsMock.Object, Options.Create(_sqsOptions),
            NullLogger<DeliveryEventDispatcher>.Instance, new DispatcherReadinessCheck());

    private static Message CreateSqsMessage(string body, int receiveCount = 1) => new()
    {
        Body = body,
        ReceiptHandle = "receipt-1",
        MessageId = "msg-1",
        Attributes = new Dictionary<string, string> { ["ApproximateReceiveCount"] = receiveCount.ToString() }
    };

    private static string ValidBody() => JsonSerializer.Serialize(new DeliveryEventMessage
    {
        EventType = DeliveryEventType.DroppedOff,
        EventId = "evt-1",
        LocationNumber = 12345,
        ClientName = "PJI",
        CorrelationId = Guid.NewGuid(),
        Payload = JsonSerializer.SerializeToElement(new { OrderId = 999L, IsInternal = false })
    });

    [Theory]
    [InlineData(ProcessResult.Success)]
    [InlineData(ProcessResult.Discard)]
    public async Task OnMessageReceivedAsync_ShouldDeleteMessage_WhenProcessorReturnsSuccessOrDiscard(ProcessResult result)
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody());
        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        _sqsMock.Setup(s => s.DeleteMessageAsync(QueueUrl, "receipt-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());
        var dispatcher = CreateDispatcher();

        // Act
        await dispatcher.OnMessageReceivedAsync(msg, CancellationToken.None);

        // Assert
        _sqsMock.Verify(s => s.DeleteMessageAsync(QueueUrl, "receipt-1", It.IsAny<CancellationToken>()), Times.Once);
        _sqsMock.Verify(s => s.ChangeMessageVisibilityAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnMessageReceivedAsync_ShouldSetZeroVisibilityAndNotDelete_WhenProcessorReturnsNonRetriable()
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody());
        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.NonRetriable);
        _sqsMock.Setup(s => s.ChangeMessageVisibilityAsync(QueueUrl, "receipt-1", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeMessageVisibilityResponse());
        var dispatcher = CreateDispatcher();

        // Act
        await dispatcher.OnMessageReceivedAsync(msg, CancellationToken.None);

        // Assert
        _sqsMock.Verify(s => s.ChangeMessageVisibilityAsync(QueueUrl, "receipt-1", 0, It.IsAny<CancellationToken>()), Times.Once);
        _sqsMock.Verify(s => s.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnMessageReceivedAsync_ShouldApplyBackoffAndNotDeleteMessage_WhenProcessorReturnsRetriable()
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody(), receiveCount: 1);
        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Retriable);
        int? capturedDelay = null;
        _sqsMock.Setup(s => s.ChangeMessageVisibilityAsync(QueueUrl, "receipt-1", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int?, CancellationToken>((_, _, d, _) => capturedDelay = d)
            .ReturnsAsync(new ChangeMessageVisibilityResponse());
        var dispatcher = CreateDispatcher();

        // Act
        await dispatcher.OnMessageReceivedAsync(msg, CancellationToken.None);

        // Assert
        capturedDelay.Should().BeInRange(24, 36); // 30s ±20%
        _sqsMock.Verify(s => s.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnMessageReceivedAsync_ShouldDeleteMessage_WhenBodyCannotBeDeserialized()
    {
        // Arrange
        var msg = CreateSqsMessage("{ this is not valid json");
        _sqsMock.Setup(s => s.DeleteMessageAsync(QueueUrl, "receipt-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());
        var dispatcher = CreateDispatcher();

        // Act
        await dispatcher.OnMessageReceivedAsync(msg, CancellationToken.None);

        // Assert
        _sqsMock.Verify(s => s.DeleteMessageAsync(QueueUrl, "receipt-1", It.IsAny<CancellationToken>()), Times.Once);
        _processorMock.Verify(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnMessageReceivedAsync_ShouldDeleteMessage_WhenEventTypeHasNoRegisteredProcessor()
    {
        // Arrange
        var unknownEventBody = JsonSerializer.Serialize(new DeliveryEventMessage
        {
            EventType = "unknown-event",
            EventId = "evt-x",
            LocationNumber = 1,
            Payload = JsonSerializer.SerializeToElement(new { })
        });
        var msg = CreateSqsMessage(unknownEventBody);
        _sqsMock.Setup(s => s.DeleteMessageAsync(QueueUrl, "receipt-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());
        var dispatcher = CreateDispatcher();

        // Act
        await dispatcher.OnMessageReceivedAsync(msg, CancellationToken.None);

        // Assert
        _sqsMock.Verify(s => s.DeleteMessageAsync(QueueUrl, "receipt-1", It.IsAny<CancellationToken>()), Times.Once);
        _processorMock.Verify(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(1, 24, 36)]   // 30s ±20%
    [InlineData(2, 48, 72)]   // 60s ±20%
    [InlineData(3, 96, 144)]  // 120s ±20%
    [InlineData(10, 240, 360)] // base capped at 300s, jitter ±20% applied after → [240, 360]
    public void CalculateRetryVisibilityTimeout_ShouldReturnDelayInJitterRange(int receiveCount, int min, int max)
    {
        // Act
        var result = DeliveryEventDispatcher.CalculateRetryVisibilityTimeout(receiveCount);

        // Assert
        result.Should().BeInRange(min, max);
    }
}
