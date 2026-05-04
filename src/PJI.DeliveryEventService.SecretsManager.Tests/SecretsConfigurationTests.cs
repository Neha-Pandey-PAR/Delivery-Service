using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace PJI.DeliveryEventService.SecretsManager.Tests;

public class SecretsConfigurationTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> data) =>
        new ConfigurationBuilder().AddInMemoryCollection(data).Build();

    [Fact]
    public void GetSecretIds_ShouldReturnEmptyDictionary_WhenNoSecretsSection()
    {
        // Arrange
        var configuration = BuildConfig(new Dictionary<string, string?>());

        // Act
        var result = configuration.GetSecretIds(rotationalSecretsOnly: false, startupSecretsOnly: false);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetSecretIds_ShouldReturnAllSecrets_WhenNoFiltersApplied()
    {
        // Arrange
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Secrets:Foo:Name"] = "foo-secret-id",
            ["Secrets:Foo:LoadOnStartup"] = "true",
            ["Secrets:Foo:RotationEnabled"] = "false",
            ["Secrets:Bar:Name"] = "bar-secret-id",
            ["Secrets:Bar:LoadOnStartup"] = "false",
            ["Secrets:Bar:RotationEnabled"] = "true"
        });

        // Act
        var result = configuration.GetSecretIds(rotationalSecretsOnly: false, startupSecretsOnly: false);

        // Assert
        result.Should().HaveCount(2);
        result["Foo"].Should().Be("foo-secret-id");
        result["Bar"].Should().Be("bar-secret-id");
    }

    [Fact]
    public void GetSecretIds_ShouldFilterToStartupSecretsOnly_WhenStartupFlagIsTrue()
    {
        // Arrange
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Secrets:Startup:Name"] = "startup-id",
            ["Secrets:Startup:LoadOnStartup"] = "true",
            ["Secrets:Lazy:Name"] = "lazy-id",
            ["Secrets:Lazy:LoadOnStartup"] = "false"
        });

        // Act
        var result = configuration.GetSecretIds(rotationalSecretsOnly: false, startupSecretsOnly: true);

        // Assert
        result.Should().ContainKey("Startup").And.NotContainKey("Lazy");
    }

    [Fact]
    public void GetSecretIds_ShouldFilterToRotationalSecretsOnly_WhenRotationFlagIsTrue()
    {
        // Arrange
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Secrets:Rotating:Name"] = "rot-id",
            ["Secrets:Rotating:RotationEnabled"] = "true",
            ["Secrets:Static:Name"] = "static-id",
            ["Secrets:Static:RotationEnabled"] = "false"
        });

        // Act
        var result = configuration.GetSecretIds(rotationalSecretsOnly: true, startupSecretsOnly: false);

        // Assert
        result.Should().ContainKey("Rotating").And.NotContainKey("Static");
    }

    [Fact]
    public void GetSecretIds_ShouldExcludeEntriesWithBlankName()
    {
        // Arrange
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Secrets:Empty:Name"] = "",
            ["Secrets:Whitespace:Name"] = "   ",
            ["Secrets:Real:Name"] = "real-id"
        });

        // Act
        var result = configuration.GetSecretIds(rotationalSecretsOnly: false, startupSecretsOnly: false);

        // Assert
        result.Should().ContainSingle().Which.Key.Should().Be("Real");
    }

    [Fact]
    public void GetSecretIds_ShouldApplyBothFiltersCombined_WhenStartupAndRotationFlagsBothSet()
    {
        // Arrange
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["Secrets:Both:Name"] = "both-id",
            ["Secrets:Both:LoadOnStartup"] = "true",
            ["Secrets:Both:RotationEnabled"] = "true",
            ["Secrets:OnlyStartup:Name"] = "startup-id",
            ["Secrets:OnlyStartup:LoadOnStartup"] = "true",
            ["Secrets:OnlyStartup:RotationEnabled"] = "false",
            ["Secrets:OnlyRotation:Name"] = "rot-id",
            ["Secrets:OnlyRotation:LoadOnStartup"] = "false",
            ["Secrets:OnlyRotation:RotationEnabled"] = "true"
        });

        // Act
        var result = configuration.GetSecretIds(rotationalSecretsOnly: true, startupSecretsOnly: true);

        // Assert
        result.Should().ContainSingle().Which.Key.Should().Be("Both");
    }
}
