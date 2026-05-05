using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PJI.DeliveryEventService.CloudApiClient;
using PJI.DeliveryEventService.Dispatcher;
using PJI.DeliveryEventService.Messaging.Models;

namespace PJI.DeliveryEventService.Tests;

public class ServiceCollectionExtensionsTests
{
    private static IConfiguration CreateMinimalConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudApiOrderService:BaseUrl"] = "https://test.api.example.com/",
                ["CloudApiOrderService:TimeoutSeconds"] = "30",
                ["Serilog:MinimumLogLevel"] = "Information",
                ["Serilog:MinimumLogLevelForConsole"] = "Information",
                ["HmacClients:Clients:TestClient:AccessToken"] = "test-token",
                ["Locations:Items:0:LocationNumber"] = "123",
                ["Locations:Items:0:LocationToken"] = "loc-token"
            })
            .Build();

    #region AddSqsLambdaServices

    [Fact]
    public void AddSqsLambdaServices_ShouldRegisterSqsMessageHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateMinimalConfiguration();

        // Act
        services.AddSqsLambdaServices(configuration);
        var sp = services.BuildServiceProvider();

        // Assert
        using var scope = sp.CreateScope();
        var handler = scope.ServiceProvider.GetService<ISqsMessageHandler>();
        handler.Should().NotBeNull();
        handler.Should().BeOfType<SqsMessageHandler>();
    }

    [Fact]
    public void AddSqsLambdaServices_ShouldRegisterDeliveryEventProcessors()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateMinimalConfiguration();

        // Act
        services.AddSqsLambdaServices(configuration);
        var sp = services.BuildServiceProvider();

        // Assert
        using var scope = sp.CreateScope();
        var processors = scope.ServiceProvider.GetServices<IDeliveryEventProcessor>().ToList();
        processors.Should().NotBeEmpty();
        processors.Should().ContainSingle(p => p.EventType == DeliveryEventType.DroppedOff);
    }

    [Fact]
    public void AddSqsLambdaServices_ShouldRegisterCloudApiOrderServiceClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateMinimalConfiguration();

        // Act
        services.AddSqsLambdaServices(configuration);
        var sp = services.BuildServiceProvider();

        // Assert
        using var scope = sp.CreateScope();
        var client = scope.ServiceProvider.GetService<ICloudApiOrderServiceClient>();
        client.Should().NotBeNull();
        client.Should().BeOfType<CloudApiOrderServiceClient>();
    }

    [Fact]
    public void AddSqsLambdaServices_ShouldRegisterLoggerFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateMinimalConfiguration();

        // Act
        services.AddSqsLambdaServices(configuration);
        var sp = services.BuildServiceProvider();

        // Assert
        var loggerFactory = sp.GetService<ILoggerFactory>();
        loggerFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddSqsLambdaServices_ShouldAllowResolvingSqsMessageHandlerWithDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateMinimalConfiguration();

        // Act
        services.AddSqsLambdaServices(configuration);
        var sp = services.BuildServiceProvider();

        // Assert - verifies full dependency graph resolves without exception
        using var scope = sp.CreateScope();
        var action = () => scope.ServiceProvider.GetRequiredService<ISqsMessageHandler>();
        action.Should().NotThrow();
    }

    [Fact]
    public void AddSqsLambdaServices_ShouldRegisterScopedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateMinimalConfiguration();
        services.AddSqsLambdaServices(configuration);
        var sp = services.BuildServiceProvider();

        // Act - resolve handler in two different scopes
        ISqsMessageHandler handler1, handler2;
        using (var scope1 = sp.CreateScope())
        {
            handler1 = scope1.ServiceProvider.GetRequiredService<ISqsMessageHandler>();
        }
        using (var scope2 = sp.CreateScope())
        {
            handler2 = scope2.ServiceProvider.GetRequiredService<ISqsMessageHandler>();
        }

        // Assert - different scopes should get different instances
        handler1.Should().NotBeSameAs(handler2);
    }

    #endregion

    #region Configuration Binding

    [Fact]
    public void AddSqsLambdaServices_ShouldBindHmacClientsOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudApiOrderService:BaseUrl"] = "https://test.api.example.com/",
                ["CloudApiOrderService:TimeoutSeconds"] = "30",
                ["Serilog:MinimumLogLevel"] = "Information",
                ["Serilog:MinimumLogLevelForConsole"] = "Information",
                ["HmacClients:Clients:PJI:AccessToken"] = "pji-access-token",
                ["Locations:Items:0:LocationNumber"] = "123",
                ["Locations:Items:0:LocationToken"] = "loc-token"
            })
            .Build();

        // Act
        services.AddSqsLambdaServices(configuration);
        var sp = services.BuildServiceProvider();

        // Assert
        using var scope = sp.CreateScope();
        var optionsMonitor = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Configuration.HmacClientsOptions>>();
        optionsMonitor.CurrentValue.Clients.Should().ContainKey("PJI");
        optionsMonitor.CurrentValue.Clients["PJI"].AccessToken.Should().Be("pji-access-token");
    }

    [Fact]
    public void AddSqsLambdaServices_ShouldBindLocationOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudApiOrderService:BaseUrl"] = "https://test.api.example.com/",
                ["CloudApiOrderService:TimeoutSeconds"] = "30",
                ["Serilog:MinimumLogLevel"] = "Information",
                ["Serilog:MinimumLogLevelForConsole"] = "Information",
                ["HmacClients:Clients:TestClient:AccessToken"] = "test-token",
                ["Locations:Items:0:LocationNumber"] = "327",
                ["Locations:Items:0:LocationToken"] = "location-token-327"
            })
            .Build();

        // Act
        services.AddSqsLambdaServices(configuration);
        var sp = services.BuildServiceProvider();

        // Assert
        using var scope = sp.CreateScope();
        var optionsMonitor = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Configuration.LocationOptions>>();
        optionsMonitor.CurrentValue.Items.Should().ContainSingle(l => l.LocationNumber == 327);
        optionsMonitor.CurrentValue.Items.First().LocationToken.Should().Be("location-token-327");
    }

    #endregion
}
