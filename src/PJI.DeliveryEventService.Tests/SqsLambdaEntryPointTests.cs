using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PJI.DeliveryEventService.Dispatcher;

namespace PJI.DeliveryEventService.Tests;

public class SqsLambdaEntryPointTests
{
    private readonly Mock<ISqsMessageHandler> _handlerMock = new(MockBehavior.Strict);
    private readonly Mock<ILambdaContext> _contextMock = new();

    public SqsLambdaEntryPointTests()
    {
        _contextMock.SetupGet(c => c.AwsRequestId).Returns("test-request-id");
        _contextMock.SetupGet(c => c.RemainingTime).Returns(TimeSpan.FromMinutes(1));
    }

    private SqsLambdaEntryPoint CreateEntryPoint()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_handlerMock.Object);
        services.AddSingleton<ILogger<SqsLambdaEntryPoint>>(NullLogger<SqsLambdaEntryPoint>.Instance);
        var sp = services.BuildServiceProvider();
        return new SqsLambdaEntryPoint(sp);
    }

    private static SQSEvent CreateSqsEvent(params string[] messageIds) => new()
    {
        Records = messageIds.Select(id => new SQSEvent.SQSMessage
        {
            MessageId = id,
            Body = "{}"
        }).ToList()
    };

    #region Empty/Null Records

    [Fact]
    public async Task FunctionHandlerAsync_ShouldReturnEmptyFailures_WhenNoRecords()
    {
        // Arrange
        var sqsEvent = new SQSEvent { Records = [] };
        var entryPoint = CreateEntryPoint();

        // Act
        var result = await entryPoint.FunctionHandlerAsync(sqsEvent, _contextMock.Object);

        // Assert
        result.BatchItemFailures.Should().BeEmpty();
        _handlerMock.Verify(h => h.HandleMessageAsync(It.IsAny<SQSEvent.SQSMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FunctionHandlerAsync_ShouldReturnEmptyFailures_WhenRecordsIsNull()
    {
        // Arrange
        var sqsEvent = new SQSEvent { Records = null };
        var entryPoint = CreateEntryPoint();

        // Act
        var result = await entryPoint.FunctionHandlerAsync(sqsEvent, _contextMock.Object);

        // Assert
        result.BatchItemFailures.Should().BeEmpty();
    }

    #endregion

    #region Success Scenarios

    [Fact]
    public async Task FunctionHandlerAsync_ShouldReturnEmptyFailures_WhenAllMessagesSucceed()
    {
        // Arrange
        var sqsEvent = CreateSqsEvent("msg-1", "msg-2", "msg-3");
        _handlerMock.Setup(h => h.HandleMessageAsync(It.IsAny<SQSEvent.SQSMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Success);
        var entryPoint = CreateEntryPoint();

        // Act
        var result = await entryPoint.FunctionHandlerAsync(sqsEvent, _contextMock.Object);

        // Assert
        result.BatchItemFailures.Should().BeEmpty();
        _handlerMock.Verify(h => h.HandleMessageAsync(It.IsAny<SQSEvent.SQSMessage>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Theory]
    [InlineData(ProcessResult.Success)]
    [InlineData(ProcessResult.Discard)]
    [InlineData(ProcessResult.NonRetriable)]
    public async Task FunctionHandlerAsync_ShouldNotAddToFailures_WhenResultIsNotRetriable(ProcessResult handlerResult)
    {
        // Arrange
        var sqsEvent = CreateSqsEvent("msg-1");
        _handlerMock.Setup(h => h.HandleMessageAsync(It.IsAny<SQSEvent.SQSMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResult);
        var entryPoint = CreateEntryPoint();

        // Act
        var result = await entryPoint.FunctionHandlerAsync(sqsEvent, _contextMock.Object);

        // Assert
        result.BatchItemFailures.Should().BeEmpty();
    }

    #endregion

    #region Partial Batch Failures

    [Fact]
    public async Task FunctionHandlerAsync_ShouldAddToFailures_WhenHandlerReturnsRetriable()
    {
        // Arrange
        var sqsEvent = CreateSqsEvent("msg-1");
        _handlerMock.Setup(h => h.HandleMessageAsync(It.IsAny<SQSEvent.SQSMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Retriable);
        var entryPoint = CreateEntryPoint();

        // Act
        var result = await entryPoint.FunctionHandlerAsync(sqsEvent, _contextMock.Object);

        // Assert
        result.BatchItemFailures.Should().ContainSingle()
            .Which.ItemIdentifier.Should().Be("msg-1");
    }

    [Fact]
    public async Task FunctionHandlerAsync_ShouldReturnOnlyRetriableFailures_WhenMixedResults()
    {
        // Arrange
        var sqsEvent = CreateSqsEvent("msg-success", "msg-retriable", "msg-discard", "msg-nonretriable");
        _handlerMock.Setup(h => h.HandleMessageAsync(
                It.Is<SQSEvent.SQSMessage>(m => m.MessageId == "msg-success"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Success);
        _handlerMock.Setup(h => h.HandleMessageAsync(
                It.Is<SQSEvent.SQSMessage>(m => m.MessageId == "msg-retriable"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Retriable);
        _handlerMock.Setup(h => h.HandleMessageAsync(
                It.Is<SQSEvent.SQSMessage>(m => m.MessageId == "msg-discard"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Discard);
        _handlerMock.Setup(h => h.HandleMessageAsync(
                It.Is<SQSEvent.SQSMessage>(m => m.MessageId == "msg-nonretriable"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.NonRetriable);
        var entryPoint = CreateEntryPoint();

        // Act
        var result = await entryPoint.FunctionHandlerAsync(sqsEvent, _contextMock.Object);

        // Assert
        result.BatchItemFailures.Should().ContainSingle()
            .Which.ItemIdentifier.Should().Be("msg-retriable");
    }

    [Fact]
    public async Task FunctionHandlerAsync_ShouldReturnMultipleFailures_WhenMultipleRetriable()
    {
        // Arrange
        var sqsEvent = CreateSqsEvent("msg-1", "msg-2", "msg-3");
        _handlerMock.Setup(h => h.HandleMessageAsync(
                It.Is<SQSEvent.SQSMessage>(m => m.MessageId == "msg-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Retriable);
        _handlerMock.Setup(h => h.HandleMessageAsync(
                It.Is<SQSEvent.SQSMessage>(m => m.MessageId == "msg-2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Success);
        _handlerMock.Setup(h => h.HandleMessageAsync(
                It.Is<SQSEvent.SQSMessage>(m => m.MessageId == "msg-3"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessResult.Retriable);
        var entryPoint = CreateEntryPoint();

        // Act
        var result = await entryPoint.FunctionHandlerAsync(sqsEvent, _contextMock.Object);

        // Assert
        result.BatchItemFailures.Should().HaveCount(2);
        result.BatchItemFailures.Select(f => f.ItemIdentifier).Should().BeEquivalentTo(["msg-1", "msg-3"]);
    }

    #endregion

    #region Cancellation Token

    [Fact]
    public async Task FunctionHandlerAsync_ShouldPassCancellationTokenToHandler()
    {
        // Arrange
        var sqsEvent = CreateSqsEvent("msg-1");
        CancellationToken capturedToken = default;
        _handlerMock.Setup(h => h.HandleMessageAsync(It.IsAny<SQSEvent.SQSMessage>(), It.IsAny<CancellationToken>()))
            .Callback<SQSEvent.SQSMessage, CancellationToken>((_, ct) => capturedToken = ct)
            .ReturnsAsync(ProcessResult.Success);
        var entryPoint = CreateEntryPoint();

        // Act
        await entryPoint.FunctionHandlerAsync(sqsEvent, _contextMock.Object);

        // Assert
        capturedToken.Should().NotBe(default(CancellationToken));
        capturedToken.CanBeCanceled.Should().BeTrue();
    }

    #endregion
}
