using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.Models.v1;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace PJI.DeliveryEventService.Authentication;

public class HmacAuthenticationHandler(
    IOptionsMonitor<HmacAuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptionsMonitor<HmacClientsOptions> hmacClientsOptions)
    : AuthenticationHandler<HmacAuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const int TimestampToleranceSeconds = 300;
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc7235#section-3.1";
    private const string DefaultUnauthorizedMessage = "Unauthorized";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.Fail("Missing Authorization header");

        var header = authHeader.ToString();
        if (!header.StartsWith("HMAC ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Invalid authorization scheme");

        var parts = header["HMAC ".Length..].Split(':');
        if (parts.Length != 5)
            return AuthenticateResult.Fail("Malformed HMAC authorization header");

        var clientName = parts[0];
        var keyId = parts[1];
        var timestampStr = parts[2];
        var nonce = parts[3];
        var providedSignature = parts[4];

        if (!long.TryParse(timestampStr, out var timestamp))
            return AuthenticateResult.Fail("Invalid timestamp");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > TimestampToleranceSeconds)
            return AuthenticateResult.Fail("Timestamp expired or in future");

        var clients = hmacClientsOptions.CurrentValue.Clients;
        if (!clients.TryGetValue(clientName, out var clientConfig))
            return AuthenticateResult.Fail("Unknown client");

        var key = clientConfig.Keys.FirstOrDefault(k => k.KeyId == keyId && k.IsActive);
        if (key is null)
            return AuthenticateResult.Fail("Unknown or inactive key");

        Request.EnableBuffering();
        var bodyBytes = await ReadBodyBytesAsync();
        Request.Body.Position = 0;

        var bodyHash = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();
        var signingString = string.Join('\n',
            Request.Method,
            Request.Path + Request.QueryString,
            timestampStr,
            nonce,
            bodyHash);

        var secretBytes = Convert.FromBase64String(key.Secret);
        var computedSignature = Convert.ToHexString(
            HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(signingString))
        ).ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(providedSignature)))
            return AuthenticateResult.Fail("Invalid signature");

        var claims = new[] { new Claim("client_name", clientName) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Re-fetch the cached authentication result to surface the failure message.
        var authResult = await Context.AuthenticateAsync(Scheme.Name);
        var message = authResult.Failure?.Message ?? DefaultUnauthorizedMessage;

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = DefaultUnauthorizedMessage,
            Type = UnauthorizedType,
            Detail = message
        };
        problemDetails.Extensions["errors"] = new ErrorResponse
        {
            Code = DefaultUnauthorizedMessage,
            Message = message
        };

        await Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json");
    }

    private async Task<byte[]> ReadBodyBytesAsync()
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        return ms.ToArray();
    }
}
