using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Handlers.DroppedOff;
using PJI.DeliveryEventService.Handlers.DroppedOff.Models;
using PJI.DeliveryEventService.Infrastructure;
using PJI.DeliveryEventService.Messaging;
using PJI.DeliveryEventService.Messaging.Models;

namespace PJI.DeliveryEventService.Tests.Handlers.DroppedOff;

public class DroppedOffEventHandlerTests
{
    private readonly Mock<IDeliveryEventQueue> _queueMock = new(MockBehavior.Strict);
    private readonly LocationOptions _locationOptions = new()
    {
        Items =
        [
            new Location { LocationNumber = 12345, LocationToken = "loc-token-12345" }
        ]
    };

    private DroppedOffEventHandler CreateHandler() =>
        new(_queueMock.Object, Options.Create(_locationOptions), NullLogger<DroppedOffEventHandler>.Instance);

    [Fact]
    public async Task HandleAsync_ShouldReturnSuccessAndEnqueue_When3PDAndLocationFound()
    {
        // Arrange
        var request = new DroppedOffEventRequest
        {
            OrderId = 999,
            EventId = "evt-1",
            LocationNumber = 12345,
            IsInternal = false,
            ClientName = "PJI",
            CorrelationId = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };

        DeliveryEventMessage? captured = null;
        string? capturedGroupId = null;
        string? capturedDedupId = null;
        _queueMock.Setup(q => q.EnqueueAsync(
                It.IsAny<DeliveryEventMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryEventMessage, string, string, CancellationToken>(
                (m, g, d, _) => { captured = m; capturedGroupId = g; capturedDedupId = d; })
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();

        // Act
        var reply = await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        reply.Success.Should().BeTrue();
        reply.ErrorCode.Should().BeNull();
        captured.Should().NotBeNull();
        captured!.EventType.Should().Be(DeliveryEventType.DroppedOff);
        captured.LocationNumber.Should().Be(12345);
        captured.EventId.Should().Be("evt-1");
        capturedGroupId.Should().Be("999");
        capturedDedupId.Should().Be("evt-1");
        _queueMock.VerifyAll();
        _queueMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnSuccessWithNoEnqueue_When1PD()
    {
        // Arrange
        var request = new DroppedOffEventRequest
        {
            OrderId = 1,
            EventId = "evt-2",
            LocationNumber = 12345,
            IsInternal = true,
            EmployeeId = 42
        };
        var handler = CreateHandler();

        // Act
        var reply = await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        reply.Success.Should().BeTrue();
        reply.ErrorCode.Should().BeNull();
        _queueMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnLocationNotFound_WhenLocationNumberNotInOptions()
    {
        // Arrange
        var request = new DroppedOffEventRequest
        {
            OrderId = 1,
            EventId = "evt-3",
            LocationNumber = 99999,
            IsInternal = false
        };
        var handler = CreateHandler();

        // Act
        var reply = await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        reply.Success.Should().BeFalse();
        reply.ErrorCode.Should().Be(ErrorCodes.LocationNotFound);
        _queueMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_ShouldPropagateClientNameAndCorrelationIdToMessage_When3PD()
    {
        // Arrange
        var correlationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var request = new DroppedOffEventRequest
        {
            OrderId = 5,
            EventId = "evt-4",
            LocationNumber = 12345,
            IsInternal = false,
            ClientName = "Acme",
            CorrelationId = correlationId
        };

        DeliveryEventMessage? captured = null;
        _queueMock.Setup(q => q.EnqueueAsync(
                It.IsAny<DeliveryEventMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryEventMessage, string, string, CancellationToken>(
                (m, _, _, _) => captured = m)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();

        // Act
        await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        captured!.ClientName.Should().Be("Acme");
        captured.CorrelationId.Should().Be(correlationId);
        captured.EventId.Should().Be("evt-4");
        _queueMock.VerifyAll();
        _queueMock.VerifyNoOtherCalls();
    }
}
