using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PJI.DeliveryEventService.SecretsManager.Refresh;

namespace PJI.DeliveryEventService.SecretsManager.Tests.Refresh;

public class SecretsManagerRefreshServiceTests
{
    private const string SECRETS_SECTION_NAME = "Secrets";
    private const string CONNECTION_STRING_SECRET_KEY = "ConnectionString";
    private const string SECRETS_NAME_KEY = "Name";
    private const string SECRETS_ROTATION_FLAG_KEY = "RotationEnabled";
    private const string AWS_RESPONSE = "{\"username\":\"domain-service\",\"password\":\"4654654654\",\"port\":\"5432\",\"engine\":\"postgres\",\"dbname\":\"domain\"}";

    private SecretsManagerRefreshService _classUnderTest;

    private readonly SecretsManagerRefreshOptions _options;
    private readonly Mock<ISecretsManagerRefreshHandler> _refreshHandlerMock;
    private readonly Mock<IAmazonSecretsManager> _secretsManagerMock;

    public SecretsManagerRefreshServiceTests()
    {
        _options = new() { RefreshInterval = TimeSpan.FromMilliseconds(30) };
        _refreshHandlerMock = new(MockBehavior.Strict);
        _secretsManagerMock = new(MockBehavior.Strict);
        _classUnderTest = CreateClassUnderTest(CreateConfiguration(new Dictionary<string, string?>
        {
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true"
        }));
    }

    public static TheoryData<IOptions<SecretsManagerRefreshOptions>?, ISecretsManagerRefreshHandler?, IAmazonSecretsManager?, IConfiguration?, ILogger<SecretsManagerRefreshService>?, string> ConstructorNullArgumentTestData() =>
        new()
        {
            { null, new Mock<ISecretsManagerRefreshHandler>().Object, new Mock<IAmazonSecretsManager>().Object, new Mock<IConfiguration>().Object, new TestLogger<SecretsManagerRefreshService>(), "options" },
            { Options.Create(new SecretsManagerRefreshOptions()), null, new Mock<IAmazonSecretsManager>().Object, new Mock<IConfiguration>().Object, new TestLogger<SecretsManagerRefreshService>(), "refreshHandler" },
            { Options.Create(new SecretsManagerRefreshOptions()), new Mock<ISecretsManagerRefreshHandler>().Object, null, new Mock<IConfiguration>().Object, new TestLogger<SecretsManagerRefreshService>(), "secretsManager" },
            { Options.Create(new SecretsManagerRefreshOptions()), new Mock<ISecretsManagerRefreshHandler>().Object, new Mock<IAmazonSecretsManager>().Object, null, new TestLogger<SecretsManagerRefreshService>(), "configuration" },
            { Options.Create(new SecretsManagerRefreshOptions()), new Mock<ISecretsManagerRefreshHandler>().Object, new Mock<IAmazonSecretsManager>().Object, new Mock<IConfiguration>().Object, null, "logger" }
        };

    [Theory]
    [MemberData(nameof(ConstructorNullArgumentTestData))]
    public void Constructor_ShouldThrowArgumentNullException_WhenAnyArgumentIsNull(
        IOptions<SecretsManagerRefreshOptions>? options,
        ISecretsManagerRefreshHandler? refreshHandler,
        IAmazonSecretsManager? secretsManager,
        IConfiguration? configuration,
        ILogger<SecretsManagerRefreshService>? logger,
        string paramName)
    {
        // Act
        var exception = Record.Exception(() =>
            new SecretsManagerRefreshService(options!, refreshHandler!, secretsManager!, configuration!, logger!));

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<ArgumentNullException>(exception);
        Assert.Equal(paramName, ((ArgumentNullException)exception).ParamName);
        _refreshHandlerMock.VerifyNoOtherCalls();
        _secretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRefreshSecrets_WhenTaskNotCancelled()
    {
        // Arrange
        var configValue = new Dictionary<string, string?>
        {
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true"
        };

        _classUnderTest = CreateClassUnderTest(CreateConfiguration(configValue));

        var response = new GetSecretValueResponse { SecretString = AWS_RESPONSE };
        _secretsManagerMock
            .Setup(i => i.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _refreshHandlerMock
            .Setup(i => i.RefreshSecretsAsync(It.IsAny<IDictionary<string, string>>()))
            .Returns(Task.CompletedTask);

        // Act
        await _classUnderTest.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await _classUnderTest.StopAsync(CancellationToken.None);

        // Assert
        _secretsManagerMock.Verify(i => i.GetSecretValueAsync(
            It.Is<GetSecretValueRequest>(r => r.SecretId == CONNECTION_STRING_SECRET_KEY), It.IsAny<CancellationToken>()),
            Times.AtLeast(1));
        _refreshHandlerMock.Verify(i => i.RefreshSecretsAsync(
            It.Is<IDictionary<string, string>>(d => d.Count == 1 && d[CONNECTION_STRING_SECRET_KEY] == AWS_RESPONSE)),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotRefreshSecrets_WhenCancellationTokenCancelledImmediately()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act
        await _classUnderTest.StartAsync(cancellationTokenSource.Token);

        // Assert
        _refreshHandlerMock.VerifyNoOtherCalls();
        _secretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSecretValueAsync_ShouldReturnNull_WhenSecretsManagerThrowsException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        _classUnderTest = CreateClassUnderTest(CreateConfiguration(new Dictionary<string, string?>()));

        _secretsManagerMock
            .Setup(i => i.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(r => r.SecretId == CONNECTION_STRING_SECRET_KEY), cancellationToken))
            .Throws(new Exception("error"));

        // Act
        var secretValue = await _classUnderTest.GetSecretValueAsync(CONNECTION_STRING_SECRET_KEY, cancellationToken);

        // Assert
        Assert.Null(secretValue);
        _secretsManagerMock.Verify(i => i.GetSecretValueAsync(
            It.Is<GetSecretValueRequest>(r => r.SecretId == CONNECTION_STRING_SECRET_KEY), cancellationToken), Times.Once);
        _refreshHandlerMock.VerifyNoOtherCalls();
        _secretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSecretValueAsync_ShouldReturnSecretValue_WhenSuccess()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        _classUnderTest = CreateClassUnderTest(CreateConfiguration(new Dictionary<string, string?>()));

        var response = new GetSecretValueResponse { SecretString = AWS_RESPONSE };
        _secretsManagerMock
            .Setup(i => i.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(r => r.SecretId == CONNECTION_STRING_SECRET_KEY), cancellationToken))
            .ReturnsAsync(response);

        // Act
        var secretValue = await _classUnderTest.GetSecretValueAsync(CONNECTION_STRING_SECRET_KEY, cancellationToken);

        // Assert
        Assert.Equal(response.SecretString, secretValue);
        _secretsManagerMock.Verify(i => i.GetSecretValueAsync(
            It.Is<GetSecretValueRequest>(r => r.SecretId == CONNECTION_STRING_SECRET_KEY), cancellationToken), Times.Once);
        _refreshHandlerMock.VerifyNoOtherCalls();
        _secretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSecretsAsync_ShouldReturnSecretValueCollection()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var configValue = new Dictionary<string, string?>
        {
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true"
        };
        _classUnderTest = CreateClassUnderTest(CreateConfiguration(configValue));

        var response = new GetSecretValueResponse { SecretString = AWS_RESPONSE };
        _secretsManagerMock
            .Setup(i => i.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var secrets = await _classUnderTest.GetSecretsAsync(cancellationToken);

        // Assert
        Assert.Single(secrets);
        Assert.Equal(response.SecretString, secrets[CONNECTION_STRING_SECRET_KEY]);
        _secretsManagerMock.Verify(i => i.GetSecretValueAsync(
            It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshHandlerMock.VerifyNoOtherCalls();
        _secretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSecretsAsync_ShouldReturnEmptySecretCollection_WhenConfigurationContainsNoSecrets()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        _classUnderTest = CreateClassUnderTest(CreateConfiguration(new Dictionary<string, string?>()));

        // Act
        var secrets = await _classUnderTest.GetSecretsAsync(cancellationToken);

        // Assert
        Assert.Empty(secrets);
        _refreshHandlerMock.VerifyNoOtherCalls();
        _secretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeRefreshHandlerAsync_ShouldCatchExceptions_WhenHandlerThrows()
    {
        // Arrange
        var secrets = new Dictionary<string, string>
        {
            [CONNECTION_STRING_SECRET_KEY] = "value"
        };

        _refreshHandlerMock
            .Setup(i => i.RefreshSecretsAsync(secrets))
            .Throws(new Exception("error"));

        // Act
        await _classUnderTest.InvokeRefreshHandlerAsync(secrets);

        // Assert
        _refreshHandlerMock.Verify(i => i.RefreshSecretsAsync(secrets), Times.Once);
        _refreshHandlerMock.VerifyNoOtherCalls();
        _secretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeRefreshHandlerAsync_ShouldInvokeHandler()
    {
        // Arrange
        var secrets = new Dictionary<string, string>
        {
            [CONNECTION_STRING_SECRET_KEY] = "value"
        };

        _refreshHandlerMock
            .Setup(i => i.RefreshSecretsAsync(secrets))
            .Returns(Task.CompletedTask);

        // Act
        await _classUnderTest.InvokeRefreshHandlerAsync(secrets);

        // Assert
        _refreshHandlerMock.Verify(i => i.RefreshSecretsAsync(secrets), Times.Once);
        _refreshHandlerMock.VerifyNoOtherCalls();
        _secretsManagerMock.VerifyNoOtherCalls();
    }

    private static IConfiguration CreateConfiguration(IDictionary<string, string?> configuration)
        => new ConfigurationBuilder().AddInMemoryCollection(configuration).Build();

    private SecretsManagerRefreshService CreateClassUnderTest(IConfiguration configuration)
        => new(Options.Create(_options), _refreshHandlerMock.Object, _secretsManagerMock.Object,
            configuration, NullLogger<SecretsManagerRefreshService>.Instance);
}
