using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PJI.DeliveryEventService.CloudApiClient;
using PJI.DeliveryEventService.CloudApiClient.Models;
using System.Net;

namespace PJI.DeliveryEventService.Tests.CloudApiClient;

public class CloudApiOrderServiceClientTests
{
    private static CloudApiOrderServiceClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") },
            NullLogger<CloudApiOrderServiceClient>.Instance);

    [Theory]
    [InlineData(HttpStatusCode.OK, CloseOrderResult.Success)]
    [InlineData(HttpStatusCode.BadRequest, CloseOrderResult.NonRetriable)]
    [InlineData(HttpStatusCode.Unauthorized, CloseOrderResult.NonRetriable)]
    [InlineData(HttpStatusCode.Forbidden, CloseOrderResult.NonRetriable)]
    [InlineData(HttpStatusCode.NotFound, CloseOrderResult.NonRetriable)]
    [InlineData(HttpStatusCode.Conflict, CloseOrderResult.NonRetriable)]
    [InlineData(HttpStatusCode.InternalServerError, CloseOrderResult.Retriable)]
    [InlineData(HttpStatusCode.BadGateway, CloseOrderResult.Retriable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CloseOrderResult.Retriable)]
    [InlineData(HttpStatusCode.GatewayTimeout, CloseOrderResult.Retriable)]
    public async Task CloseOrderAsync_ShouldMapStatusCodeToResult(HttpStatusCode status, CloseOrderResult expected)
    {
        // Arrange
        var handler = new StubHandler(_ => new HttpResponseMessage(status));
        var client = CreateClient(handler);

        // Act
        var result = await client.CloseOrderAsync(
            orderId: 1, accessToken: "a", locationToken: "l",
            CancellationToken.None);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task CloseOrderAsync_ShouldReturnRetriable_WhenHttpRequestExceptionThrown()
    {
        // Arrange
        var handler = new StubHandler(_ => throw new HttpRequestException("network down"));
        var client = CreateClient(handler);

        // Act
        var result = await client.CloseOrderAsync(1, "a", "l", CancellationToken.None);

        // Assert
        result.Should().Be(CloseOrderResult.Retriable);
    }

    [Fact]
    public async Task CloseOrderAsync_ShouldReturnRetriable_WhenTaskCanceledExceptionThrown()
    {
        // Arrange — TaskCanceledException not from caller-supplied token = HttpClient timeout
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        var client = CreateClient(handler);

        // Act
        var result = await client.CloseOrderAsync(1, "a", "l", CancellationToken.None);

        // Assert
        result.Should().Be(CloseOrderResult.Retriable);
    }

    [Fact]
    public async Task CloseOrderAsync_ShouldSendAllRequiredHeaders_WhenCalled()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK); });
        var client = CreateClient(handler);

        // Act
        await client.CloseOrderAsync(42, "access-x", "loc-y", CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/v1/orders/42/close");
        captured.Headers.GetValues("parpos-access-token").Should().ContainSingle().Which.Should().Be("access-x");
        captured.Headers.GetValues("parpos-location-token").Should().ContainSingle().Which.Should().Be("loc-y");
        captured.Headers.Contains("parpos-correlation-id").Should().BeFalse();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
