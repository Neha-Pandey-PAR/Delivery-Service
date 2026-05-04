using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PJI.DeliveryEventService.Configuration;
using PJI.DeliveryEventService.SecretsManager;

namespace PJI.DeliveryEventService.Tests.SecretsManager;

public class SecretsManagerRefreshHandlerTests
{
    private readonly Mock<IOptionsMonitorCache<HmacClientsOptions>> _cacheMock = new(MockBehavior.Strict);

    private SecretsManagerRefreshHandler CreateHandler() =>
        new(_cacheMock.Object, NullLogger<SecretsManagerRefreshHandler>.Instance);

    [Fact]
    public async Task RefreshSecretsAsync_ShouldDoNothing_WhenSecretsDictionaryDoesNotContainHmacClients()
    {
        // Arrange
        var secrets = new Dictionary<string, string>
        {
            ["SomeOtherSecret"] = "{}"
        };
        var handler = CreateHandler();

        // Act
        await handler.RefreshSecretsAsync(secrets);

        // Assert
        _cacheMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshSecretsAsync_ShouldClearOptionsCache_WhenHmacClientsSecretIsValid()
    {
        // Arrange
        var secrets = new Dictionary<string, string>
        {
            ["HmacClients"] = """
                {
                  "Clients": {
                    "PJI": {
                      "AccessToken": "tok",
                      "Keys": [{ "KeyId": "k1", "Secret": "c2VjcmV0", "IsActive": true }]
                    }
                  }
                }
                """
        };
        _cacheMock.Setup(c => c.TryRemove(Options.DefaultName)).Returns(true);
        var handler = CreateHandler();

        // Act
        await handler.RefreshSecretsAsync(secrets);

        // Assert
        _cacheMock.Verify(c => c.TryRemove(Options.DefaultName), Times.Once);
        _cacheMock.VerifyAll();
        _cacheMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshSecretsAsync_ShouldNotClearCache_WhenSecretJsonIsInvalid()
    {
        // Arrange — invalid JSON yields no transformed entries → cache untouched
        var secrets = new Dictionary<string, string>
        {
            ["HmacClients"] = "{ this is not valid json"
        };
        var handler = CreateHandler();

        // Act
        await handler.RefreshSecretsAsync(secrets);

        // Assert
        _cacheMock.VerifyNoOtherCalls();
    }
}
