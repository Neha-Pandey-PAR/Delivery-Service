using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PJI.DeliveryEventService.Infrastructure;
using System.Text.Json;

namespace PJI.DeliveryEventService.Tests.Infrastructure;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ShouldReturnTrue_WhenExceptionIsHandled()
    {
        // Arrange
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        // Act
        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException("boom"), CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
    }

    [Fact]
    public async Task TryHandleAsync_ShouldSetStatusCode500_WhenExceptionThrown()
    {
        // Arrange
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        // Act
        await handler.TryHandleAsync(ctx, new Exception("any"), CancellationToken.None);

        // Assert
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldWriteProblemPlusJsonContentType_WhenExceptionThrown()
    {
        // Arrange
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        // Act
        await handler.TryHandleAsync(ctx, new Exception("any"), CancellationToken.None);

        // Assert
        ctx.Response.ContentType.Should().Contain("application/problem+json");
    }

    [Fact]
    public async Task TryHandleAsync_ShouldWriteProblemDetailsBody_WhenExceptionThrown()
    {
        // Arrange
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Request.Path = "/v1/orders/1/events/dropped-off";

        // Act
        await handler.TryHandleAsync(ctx, new Exception("any"), CancellationToken.None);

        // Assert
        ctx.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<ProblemDetails>(ctx.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        body.Should().NotBeNull();
        body!.Status.Should().Be(StatusCodes.Status500InternalServerError);
        body.Title.Should().Be("Internal Server Error");
        body.Type.Should().Be("https://tools.ietf.org/html/rfc7231#section-6.6.1");
        body.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Arrange & Act
        var act = () => new GlobalExceptionHandler(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
