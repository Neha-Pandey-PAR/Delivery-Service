using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Messaging.Models;
using PJI.DeliveryEventService.Models;
using PJI.DeliveryEventService.SqsDispatcher.CloudApiClient;
using PJI.DeliveryEventService.SqsDispatcher.CloudApiClient.Models;
using PJI.DeliveryEventService.SqsDispatcher.Dispatcher;
using PJI.DeliveryEventService.SqsDispatcher.Dispatcher.Processors;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace PJI.DeliveryEventService.SqsDispatcher.Tests.Dispatcher.Processors;

public class DroppedOffEventProcessorTests
{
    private readonly Mock<ICloudApiOrderServiceClient> _cloudApiMock = new(MockBehavior.Strict);
    private readonly Mock<IOptionsMonitor<HmacClientsOptions>> _hmacMonitor = new(MockBehavior.Strict);
    private readonly Mock<IOptionsMonitor<LocationOptions>> _locationMonitor = new(MockBehavior.Strict);

    private readonly HmacClientsOptions _hmacOptions = new()
    {
        Clients =
        {
            ["PJI"] = new HmacClientConfig { AccessToken = "access-pji" }
        }
    };
    private readonly LocationOptions _locationOptions = new()
    {
        Items =
        [
            new Location { LocationNumber = 12345, LocationToken = "loc-token-12345" }
        ]
    };

    public DroppedOffEventProcessorTests()
    {
        _hmacMonitor.SetupGet(m => m.CurrentValue).Returns(_hmacOptions);
        _locationMonitor.SetupGet(m => m.CurrentValue).Returns(_locationOptions);
    }

    private DroppedOffEventProcessor CreateProcessor() =>
        new(_cloudApiMock.Object, _hmacMonitor.Object, _locationMonitor.Object,
            NullLogger<DroppedOffEventProcessor>.Instance);

    private static DeliveryEventMessage CreateMessage(string clientName = "PJI", int locationNumber = 12345, long orderId = 999) =>
        new()
        {
            EventType = DeliveryEventType.DroppedOff,
            EventId = "evt-1",
            LocationNumber = locationNumber,
            ClientName = clientName,
            CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Payload = JsonSerializer.SerializeToElement(new DroppedOffPayload
            {
                OrderId = orderId,
                IsInternal = false,
                EmployeeId = null
            })
        };

    [Fact]
    public async Task ProcessAsync_ShouldReturnSuccess_WhenCloseOrderSucceeds()
    {
        // Arrange
        var message = CreateMessage();
        _cloudApiMock.Setup(c => c.CloseOrderAsync(
                999L, "access-pji", "loc-token-12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CloseOrderResult.Success);
        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Success);
        _cloudApiMock.VerifyAll();
        _cloudApiMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnNonRetriable_WhenCloudApiReturnsNonRetriable()
    {
        // Arrange
        var message = CreateMessage();
        _cloudApiMock.Setup(c => c.CloseOrderAsync(
                999L, "access-pji", "loc-token-12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CloseOrderResult.NonRetriable);
        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.NonRetriable);
        _cloudApiMock.VerifyAll();
        _cloudApiMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnRetriable_WhenCloudApiReturnsRetriable()
    {
        // Arrange
        var message = CreateMessage();
        _cloudApiMock.Setup(c => c.CloseOrderAsync(
                999L, "access-pji", "loc-token-12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CloseOrderResult.Retriable);
        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Retriable);
        _cloudApiMock.VerifyAll();
        _cloudApiMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnDiscardWithNoApiCall_WhenClientNameMissingFromOptions()
    {
        // Arrange
        var message = CreateMessage(clientName: "UnknownClient");
        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Discard);
        _cloudApiMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnDiscardWithNoApiCall_WhenLocationMissingFromOptions()
    {
        // Arrange
        var message = CreateMessage(locationNumber: 99999);
        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Discard);
        _cloudApiMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnDiscardWithNoApiCall_WhenPayloadCannotBeDeserialized()
    {
        // Arrange — payload is an int, not a DroppedOffPayload object
        var message = new DeliveryEventMessage
        {
            EventType = DeliveryEventType.DroppedOff,
            EventId = "evt-bad",
            LocationNumber = 12345,
            ClientName = "PJI",
            CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.SerializeToElement(42)
        };
        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessResult.Discard);
        _cloudApiMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void EventType_ShouldReturnDroppedOff()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act
        var eventType = processor.EventType;

        // Assert
        eventType.Should().Be(DeliveryEventType.DroppedOff);
    }
}
