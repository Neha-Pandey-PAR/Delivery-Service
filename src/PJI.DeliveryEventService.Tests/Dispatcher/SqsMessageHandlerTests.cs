using Amazon.Lambda.SQSEvents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PJI.DeliveryEventService.Dispatcher;
using PJI.DeliveryEventService.Messaging.Models;
using System.Text.Json;

namespace PJI.DeliveryEventService.Tests.Dispatcher;

public class SqsMessageHandlerTests
{
    private readonly Mock<IDeliveryEventProcessor> _processorMock = new(MockBehavior.Strict);

    public SqsMessageHandlerTests()
    {
        _processorMock.SetupGet(p => p.EventType).Returns(DeliveryEventType.DroppedOff);
    }

    private SqsMessageHandler CreateHandler() =>
        new([_processorMock.Object], NullLogger<SqsMessageHandler>.Instance);

    private static SQSEvent.SQSMessage CreateSqsMessage(string body) => new()
    {
        Body = body,
        MessageId = "msg-1"
    };

    private static string ValidBody(string eventType = DeliveryEventType.DroppedOff) =>
        JsonSerializer.Serialize(new DeliveryEventMessage
        {
            EventType = eventType,
            EventId = "evt-1",
            LocationNumber = 12345,
            ClientName = "PJI",
            CorrelationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Payload = JsonSerializer.SerializeToElement(new { OrderId = 999L })
        });

    #region Success Scenarios

    [Fact]
    public async Task HandleMessageAsync_ShouldReturnSuccess_WhenProcessorReturnsSuccess()
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody());
        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Success);
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Success);
        _processorMock.Verify(p => p.ProcessAsync(
            It.Is<DeliveryEventMessage>(m => m.EventId == "evt-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_ShouldPassCorrectMessageToProcessor()
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody());
        DeliveryEventMessage? capturedMessage = null;
        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryEventMessage, CancellationToken>((m, _) => capturedMessage = m)
            .ReturnsAsync(ProcessResult.Success);
        var handler = CreateHandler();

        // Act
        await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.EventType.Should().Be(DeliveryEventType.DroppedOff);
        capturedMessage.EventId.Should().Be("evt-1");
        capturedMessage.LocationNumber.Should().Be(12345);
        capturedMessage.ClientName.Should().Be("PJI");
        capturedMessage.CorrelationId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    #endregion

    #region Processor Result Passthrough

    [Theory]
    [InlineData(ProcessResult.Success)]
    [InlineData(ProcessResult.Discard)]
    [InlineData(ProcessResult.NonRetriable)]
    [InlineData(ProcessResult.Retriable)]
    public async Task HandleMessageAsync_ShouldReturnProcessorResult(ProcessResult processorResult)
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody());
        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processorResult);
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert
        result.Should().Be(processorResult);
    }

    #endregion

    #region Deserialization Errors

    [Fact]
    public async Task HandleMessageAsync_ShouldReturnDiscard_WhenBodyIsInvalidJson()
    {
        // Arrange
        var msg = CreateSqsMessage("{ this is not valid json");
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Discard);
        _processorMock.Verify(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleMessageAsync_ShouldReturnDiscard_WhenBodyIsNull()
    {
        // Arrange
        var msg = CreateSqsMessage("null");
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Discard);
        _processorMock.Verify(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleMessageAsync_ShouldReturnDiscard_WhenBodyIsEmptyObject()
    {
        // Arrange - empty JSON object deserializes to non-null but with default values
        var msg = CreateSqsMessage("{}");
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert - empty EventType has no processor, so Discard
        result.Should().Be(ProcessResult.Discard);
    }

    #endregion

    #region Unknown Event Type

    [Fact]
    public async Task HandleMessageAsync_ShouldReturnDiscard_WhenEventTypeHasNoProcessor()
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody(eventType: "UnknownEventType"));
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Discard);
        _processorMock.Verify(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Exception Handling

    [Fact]
    public async Task HandleMessageAsync_ShouldReturnRetriable_WhenProcessorThrowsException()
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody());
        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Retriable);
    }

    [Fact]
    public async Task HandleMessageAsync_ShouldReturnRetriable_WhenCancellationRequested()
    {
        // Arrange
        var msg = CreateSqsMessage(ValidBody());
        using var cts = new CancellationTokenSource();
        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .Returns(async (DeliveryEventMessage _, CancellationToken ct) =>
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return ProcessResult.Success;
            });
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(msg, cts.Token);

        // Assert
        result.Should().Be(ProcessResult.Retriable);
    }

    #endregion

    #region Multiple Processors

    [Fact]
    public async Task HandleMessageAsync_ShouldRouteToCorrectProcessor_WhenMultipleProcessorsRegistered()
    {
        // Arrange
        var otherProcessorMock = new Mock<IDeliveryEventProcessor>(MockBehavior.Strict);
        otherProcessorMock.SetupGet(p => p.EventType).Returns("OtherEventType");

        _processorMock.Setup(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Success);

        var handler = new SqsMessageHandler(
            [_processorMock.Object, otherProcessorMock.Object],
            NullLogger<SqsMessageHandler>.Instance);

        var msg = CreateSqsMessage(ValidBody(DeliveryEventType.DroppedOff));

        // Act
        var result = await handler.HandleMessageAsync(msg, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Success);
        _processorMock.Verify(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        otherProcessorMock.Verify(p => p.ProcessAsync(It.IsAny<DeliveryEventMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
