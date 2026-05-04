using FluentAssertions;
using PJI.DeliveryEventService.SecretsManager;

namespace PJI.DeliveryEventService.Tests.SecretsManager;

public class SecretsTransformerTests
{
    [Fact]
    public void Transform_ShouldYieldNothing_WhenConfigurationKeyIsNotHmacClients()
    {
        // Arrange
        const string key = "SomeOtherSecret";
        const string secretValue = "{}";

        // Act
        var result = SecretsTransformer.Transform(key, secretValue).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Transform_ShouldYieldNothing_WhenSecretIsInvalidJson()
    {
        // Arrange
        const string key = "HmacClients";
        const string secretValue = "{ not valid json";

        // Act
        var result = SecretsTransformer.Transform(key, secretValue).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Transform_ShouldYieldNothing_WhenJsonHasNoClientsProperty()
    {
        // Arrange
        const string key = "HmacClients";
        const string secretValue = "{ \"foo\": \"bar\" }";

        // Act
        var result = SecretsTransformer.Transform(key, secretValue).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Transform_ShouldYieldFlattenedConfigurationKeys_ForSingleClient()
    {
        // Arrange
        const string key = "HmacClients";
        const string secretValue = """
        {
          "Clients": {
            "PJI": {
              "AccessToken": "tok-123",
              "Keys": [
                { "KeyId": "k1", "Secret": "c2VjcmV0", "IsActive": true }
              ]
            }
          }
        }
        """;

        // Act
        var result = SecretsTransformer.Transform(key, secretValue).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Assert
        result["HmacClients:Clients:PJI:AccessToken"].Should().Be("tok-123");
        result["HmacClients:Clients:PJI:Keys:0:KeyId"].Should().Be("k1");
        result["HmacClients:Clients:PJI:Keys:0:Secret"].Should().Be("c2VjcmV0");
        result["HmacClients:Clients:PJI:Keys:0:IsActive"].Should().Be("True");
    }

    [Fact]
    public void Transform_ShouldYieldFlattenedConfigurationKeys_ForMultipleClientsAndKeys()
    {
        // Arrange
        const string key = "HmacClients";
        const string secretValue = """
        {
          "Clients": {
            "PJI": {
              "AccessToken": "tok-pji",
              "Keys": [
                { "KeyId": "k1", "Secret": "c2VjcmV0MQ==", "IsActive": true },
                { "KeyId": "k2", "Secret": "c2VjcmV0Mg==", "IsActive": false }
              ]
            },
            "Acme": {
              "AccessToken": "tok-acme",
              "Keys": [
                { "KeyId": "ak1", "Secret": "YWNtZQ==", "IsActive": true }
              ]
            }
          }
        }
        """;

        // Act
        var result = SecretsTransformer.Transform(key, secretValue).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Assert
        result["HmacClients:Clients:PJI:Keys:0:KeyId"].Should().Be("k1");
        result["HmacClients:Clients:PJI:Keys:1:KeyId"].Should().Be("k2");
        result["HmacClients:Clients:PJI:Keys:1:IsActive"].Should().Be("False");
        result["HmacClients:Clients:Acme:AccessToken"].Should().Be("tok-acme");
        result["HmacClients:Clients:Acme:Keys:0:KeyId"].Should().Be("ak1");
    }

    [Fact]
    public void Transform_ShouldSkipMissingOptionalProperties_WhenKeyOrAccessTokenAbsent()
    {
        // Arrange
        const string key = "HmacClients";
        const string secretValue = """
        {
          "Clients": {
            "MinimalClient": {
              "Keys": [
                { "Secret": "c2VjcmV0" }
              ]
            }
          }
        }
        """;

        // Act
        var result = SecretsTransformer.Transform(key, secretValue).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Assert
        result.Should().NotContainKey("HmacClients:Clients:MinimalClient:AccessToken");
        result.Should().NotContainKey("HmacClients:Clients:MinimalClient:Keys:0:KeyId");
        result.Should().NotContainKey("HmacClients:Clients:MinimalClient:Keys:0:IsActive");
        result["HmacClients:Clients:MinimalClient:Keys:0:Secret"].Should().Be("c2VjcmV0");
    }
}
