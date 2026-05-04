using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PJI.DeliveryEventService.Authentication;
using PJI.DeliveryEventService.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace PJI.DeliveryEventService.Tests.Authentication;

public class HmacAuthenticationHandlerTests
{
    private const string ClientName = "PJI";
    private const string KeyId = "key-1";
    private static readonly byte[] SecretBytes = "ThisIsA32ByteSecretForHmacTests!"u8.ToArray();
    private static readonly string SecretBase64 = Convert.ToBase64String(SecretBytes);

    private readonly HmacClientsOptions _hmacOptions;

    public HmacAuthenticationHandlerTests()
    {
        _hmacOptions = new HmacClientsOptions
        {
            Clients =
            {
                [ClientName] = new HmacClientConfig
                {
                    AccessToken = "access-token",
                    Keys = [new HmacKeyConfig { KeyId = KeyId, Secret = SecretBase64, IsActive = true }]
                }
            }
        };
    }

    private async Task<HmacAuthenticationHandler> CreateHandlerAsync(HttpContext context)
    {
        var monitorMock = new Mock<IOptionsMonitor<HmacAuthenticationSchemeOptions>>(MockBehavior.Strict);
        monitorMock.Setup(m => m.Get(It.IsAny<string>())).Returns(new HmacAuthenticationSchemeOptions());

        var clientsMonitorMock = new Mock<IOptionsMonitor<HmacClientsOptions>>(MockBehavior.Strict);
        clientsMonitorMock.SetupGet(m => m.CurrentValue).Returns(_hmacOptions);

        var handler = new HmacAuthenticationHandler(
            monitorMock.Object, NullLoggerFactory.Instance, UrlEncoder.Default, clientsMonitorMock.Object);

        await handler.InitializeAsync(
            new AuthenticationScheme(HmacAuthenticationDefaults.SchemeName, null, typeof(HmacAuthenticationHandler)),
            context);

        return handler;
    }

    private static HttpContext CreateContext(string? authHeader, string method = "POST", string path = "/v1/orders/1/events/dropped-off", string body = "")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        ctx.Request.Body = new MemoryStream(bodyBytes);
        ctx.Request.ContentLength = bodyBytes.Length;
        if (authHeader is not null)
            ctx.Request.Headers.Authorization = authHeader;
        return ctx;
    }

    private static string BuildAuthHeader(string clientName, string keyId, long timestamp, string nonce, string method, string path, string body, byte[] secret)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var signingString = string.Join('\n', method, path, timestamp.ToString(), nonce, bodyHash);
        var signature = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(signingString))).ToLowerInvariant();
        return $"HMAC {clientName}:{keyId}:{timestamp}:{nonce}:{signature}";
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldSucceed_WhenValidSignatureAndActiveKey()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const string method = "POST", path = "/v1/orders/1/events/dropped-off", body = "{\"eventId\":\"x\"}";
        var auth = BuildAuthHeader(ClientName, KeyId, ts, "nonce-1", method, path, body, SecretBytes);
        var ctx = CreateContext(auth, method, path, body);
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirst("client_name")!.Value.Should().Be(ClientName);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenAuthorizationHeaderMissing()
    {
        // Arrange
        var ctx = CreateContext(authHeader: null);
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("Bearer abc")]
    [InlineData("HMAC")]
    [InlineData("HMAC a:b:c")]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenHeaderIsMalformedOrWrongScheme(string authHeader)
    {
        // Arrange
        var ctx = CreateContext(authHeader);
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenTimestampIsInFutureBeyondTolerance()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow.AddSeconds(400).ToUnixTimeSeconds();
        const string method = "POST", path = "/v1/orders/1/events/dropped-off", body = "";
        var auth = BuildAuthHeader(ClientName, KeyId, ts, "nonce-1", method, path, body, SecretBytes);
        var ctx = CreateContext(auth, method, path, body);
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenTimestampIsNotParseable()
    {
        // Arrange
        const string method = "POST", path = "/v1/orders/1/events/dropped-off";
        const string auth = "HMAC PJI:key-1:not-a-number:nonce:00";
        var ctx = CreateContext(auth, method, path, "");
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenTimestampExceeds300Seconds()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow.AddSeconds(-400).ToUnixTimeSeconds();
        const string method = "POST", path = "/v1/orders/1/events/dropped-off", body = "";
        var auth = BuildAuthHeader(ClientName, KeyId, ts, "nonce-1", method, path, body, SecretBytes);
        var ctx = CreateContext(auth, method, path, body);
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenClientNameNotFound()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const string method = "POST", path = "/v1/orders/1/events/dropped-off", body = "";
        var auth = BuildAuthHeader("UnknownClient", KeyId, ts, "n", method, path, body, SecretBytes);
        var ctx = CreateContext(auth, method, path, body);
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenKeyIdNotFound()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const string method = "POST", path = "/v1/orders/1/events/dropped-off", body = "";
        var auth = BuildAuthHeader(ClientName, "unknown-key", ts, "n", method, path, body, SecretBytes);
        var ctx = CreateContext(auth, method, path, body);
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenKeyIsInactive()
    {
        // Arrange
        _hmacOptions.Clients[ClientName].Keys[0].IsActive = false;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const string method = "POST", path = "/v1/orders/1/events/dropped-off", body = "";
        var auth = BuildAuthHeader(ClientName, KeyId, ts, "n", method, path, body, SecretBytes);
        var ctx = CreateContext(auth, method, path, body);
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_ShouldFail_WhenSignatureDoesNotMatch()
    {
        // Arrange — sign with one body, send a different body
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const string method = "POST", path = "/v1/orders/1/events/dropped-off";
        var auth = BuildAuthHeader(ClientName, KeyId, ts, "n", method, path, "original-body", SecretBytes);
        var ctx = CreateContext(auth, method, path, "tampered-body");
        var handler = await CreateHandlerAsync(ctx);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        result.Succeeded.Should().BeFalse();
    }
}
