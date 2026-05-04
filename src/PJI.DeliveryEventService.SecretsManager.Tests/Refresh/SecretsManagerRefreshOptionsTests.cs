using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.SecretsManager.Refresh;

namespace PJI.DeliveryEventService.SecretsManager.Tests.Refresh;

public class SecretsManagerRefreshOptionsTests
{
    [Fact]
    public void Validate_ShouldNotThrowException_WhenOptionsAreValid()
    {
        // Arrange
        var options = new SecretsManagerRefreshOptions { RefreshInterval = TimeSpan.FromMinutes(1) };

        // Act
        var exception = Record.Exception(options.Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ShouldThrowOptionsValidationException_WhenRefreshIntervalIsInvalid()
    {
        // Arrange
        var options = new SecretsManagerRefreshOptions { RefreshInterval = TimeSpan.Zero };

        // Act
        var exception = Record.Exception(options.Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
        var optionsValidationException = (OptionsValidationException)exception;
        Assert.Equal(typeof(SecretsManagerRefreshOptions), optionsValidationException.OptionsType);
        Assert.Equal("Refresh interval must be greater than 00:00:00.", optionsValidationException.Message);
    }
}
