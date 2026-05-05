using FluentAssertions;
using PJI.DeliveryEventService.SqsDispatcher.Infrastructure;
using System.Diagnostics;
using System.Net;

namespace PJI.DeliveryEventService.SqsDispatcher.Tests.Infrastructure;

public class TraceparentPropagationHandlerTests
{
    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var stub = new StubHandler(responder);
        var handler = new TraceparentPropagationHandler { InnerHandler = stub };
        return new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
    }

    [Fact]
    public async Task SendAsync_ShouldAddTraceparentHeader_WhenActivityIsCurrent()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var client = CreateClient(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK); });

        using var activity = new Activity("test").Start();

        // Act
        await client.GetAsync("/path");

        // Assert
        captured.Should().NotBeNull();
        captured!.Headers.Contains("traceparent").Should().BeTrue();
        captured.Headers.GetValues("traceparent").Should().ContainSingle()
            .Which.Should().Be(activity.Id);
    }

    [Fact]
    public async Task SendAsync_ShouldNotOverwriteTraceparent_WhenAlreadyPresent()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var client = CreateClient(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK); });

        using var activity = new Activity("test").Start();
        const string existingTraceparent = "00-aaaabbbbccccddddaaaabbbbccccdddd-1234567890abcdef-01";

        var request = new HttpRequestMessage(HttpMethod.Get, "/path");
        request.Headers.TryAddWithoutValidation("traceparent", existingTraceparent);

        // Act
        await client.SendAsync(request);

        // Assert
        captured!.Headers.GetValues("traceparent").Should().ContainSingle()
            .Which.Should().Be(existingTraceparent);
    }

    [Fact]
    public async Task SendAsync_ShouldNotAddTraceparentHeader_WhenNoActivityIsCurrent()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var client = CreateClient(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK); });

        Activity.Current = null;

        // Act
        await client.GetAsync("/path");

        // Assert
        captured!.Headers.Contains("traceparent").Should().BeFalse();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
