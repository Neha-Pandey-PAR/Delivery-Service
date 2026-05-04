using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Moq;
using PJI.DeliveryEventService.SecretsManager.ConfigurationProviders;
using System.Reflection;
using static PJI.DeliveryEventService.SecretsManager.ConfigurationProviders.SecretsConfigurationProvider;

namespace PJI.DeliveryEventService.SecretsManager.Tests.ConfigurationProviders;

public class SecretsConfigurationProviderTests
{
    private const string SECRETS_SECTION_NAME = "Secrets";
    private const string CONNECTION_STRING_SECRET_KEY = "ConnectionString";
    private const string SCHEMA_CONNECTION_STRING_SECRET_KEY = "SchemaManagementConnectionString";
    private const string IDENTITY_CREDENTIALS_SECRET_KEY = "IdentityCredentials";
    private const string AWS_RESPONSE = "{\"username\":\"domain-service\",\"password\":\"4654654654\",\"port\":\"5432\",\"engine\":\"postgres\",\"dbname\":\"domain\"}";
    private const string AWS_RESPONSE_IDENTITY = "{\"username\":\"domain-msk\",\"password\":\"234452\"}";
    private const string SECRETS_NAME_KEY = "Name";
    private const string SECRETS_ROTATION_FLAG_KEY = "RotationEnabled";
    private const string LOAD_ON_STARTUP_FLAG_KEY = "LoadOnStartup";
    private readonly Mock<IAmazonSecretsManager> _amazonSecretsManagerMock;

    private SecretsConfigurationProvider _classUnderTest;

    public SecretsConfigurationProviderTests()
    {
        _amazonSecretsManagerMock = new(MockBehavior.Strict);
        _classUnderTest = CreateClassUnderTest(CreateConfiguration(new Dictionary<string, string?>()));
    }

    public static TheoryData<IConfiguration?, IAmazonSecretsManager?, SecretValueTransformerDelegate?, string> ConstructorNullArgumentTestData =>
        new()
        {
            { null, new Mock<IAmazonSecretsManager>().Object, new Mock<SecretValueTransformerDelegate>().Object, "configuration" },
            { new Mock<IConfiguration>().Object, null, new Mock<SecretValueTransformerDelegate>().Object, "amazonSecretsManager" },
            { new Mock<IConfiguration>().Object, new Mock<IAmazonSecretsManager>().Object, null, "transformer" }
        };

    [Theory]
    [MemberData(nameof(ConstructorNullArgumentTestData))]
    public void Constructor_ShouldThrowArgumentNullException_WhenAnyArgumentIsNull(
        IConfiguration? configuration, IAmazonSecretsManager? amazonSecretsManager,
        SecretValueTransformerDelegate? transformer, string paramName)
    {
        // Act
        var exception = Record.Exception(() => new SecretsConfigurationProvider(configuration!, amazonSecretsManager!, transformer!));

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<ArgumentNullException>(exception);
        Assert.Equal(paramName, ((ArgumentNullException)exception).ParamName);
        _amazonSecretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void Load_ShouldBeEmpty_WhenSecretsSectionIsMissing()
    {
        // Arrange & Act
        _classUnderTest.Load();

        // Assert
        Assert.Empty(GetData());
        _amazonSecretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void Load_ShouldHaveData_WhenSecretsSectionHasSingleEntry()
    {
        // Arrange
        var configValue = new Dictionary<string, string?>
        {
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true",
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{LOAD_ON_STARTUP_FLAG_KEY}"] = "true"
        };

        _amazonSecretsManagerMock.Setup(x => x.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(i => i.SecretId == CONNECTION_STRING_SECRET_KEY), CancellationToken.None))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = AWS_RESPONSE });

        _classUnderTest = CreateClassUnderTest(CreateConfiguration(configValue));

        // Act
        _classUnderTest.Load();

        // Assert
        var data = GetData();
        Assert.NotEmpty(data);
        Assert.Single(data);

        _amazonSecretsManagerMock.Verify(x => x.GetSecretValueAsync(
            It.Is<GetSecretValueRequest>(i => i.SecretId == CONNECTION_STRING_SECRET_KEY), CancellationToken.None), Times.Once);
        _amazonSecretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void Load_ShouldHaveData_WhenSecretsSectionHasMultipleNonNullableEntries()
    {
        // Arrange
        var configValue = new Dictionary<string, string?>
        {
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true",
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{LOAD_ON_STARTUP_FLAG_KEY}"] = "true",
            [$"{SECRETS_SECTION_NAME}:{SCHEMA_CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = SCHEMA_CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{SCHEMA_CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true",
            [$"{SECRETS_SECTION_NAME}:{SCHEMA_CONNECTION_STRING_SECRET_KEY}:{LOAD_ON_STARTUP_FLAG_KEY}"] = "true",
            [$"{SECRETS_SECTION_NAME}:{IDENTITY_CREDENTIALS_SECRET_KEY}:{SECRETS_NAME_KEY}"] = IDENTITY_CREDENTIALS_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{IDENTITY_CREDENTIALS_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "false",
            [$"{SECRETS_SECTION_NAME}:{IDENTITY_CREDENTIALS_SECRET_KEY}:{LOAD_ON_STARTUP_FLAG_KEY}"] = "false"
        };

        _amazonSecretsManagerMock.Setup(x => x.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(i => i.SecretId == CONNECTION_STRING_SECRET_KEY), CancellationToken.None))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = AWS_RESPONSE });
        _amazonSecretsManagerMock.Setup(x => x.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(i => i.SecretId == SCHEMA_CONNECTION_STRING_SECRET_KEY), CancellationToken.None))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = AWS_RESPONSE });

        _classUnderTest = CreateClassUnderTest(CreateConfiguration(configValue));

        // Act
        _classUnderTest.Load();

        // Assert
        var data = GetData();
        Assert.NotEmpty(data);
        Assert.Equal(2, data.Count);

        _amazonSecretsManagerMock.Verify(x => x.GetSecretValueAsync(
            It.Is<GetSecretValueRequest>(i => i.SecretId == CONNECTION_STRING_SECRET_KEY), CancellationToken.None), Times.Once);
        _amazonSecretsManagerMock.Verify(x => x.GetSecretValueAsync(
            It.Is<GetSecretValueRequest>(i => i.SecretId == SCHEMA_CONNECTION_STRING_SECRET_KEY), CancellationToken.None), Times.Once);
        _amazonSecretsManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void Load_ShouldThrowKeyNotFoundException_WhenAwsSecretManagerThrowsResourceNotFoundException()
    {
        // Arrange
        var configValue = new Dictionary<string, string?>
        {
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true",
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{LOAD_ON_STARTUP_FLAG_KEY}"] = "true"
        };

        _amazonSecretsManagerMock.Setup(x => x.GetSecretValueAsync(
                It.IsAny<GetSecretValueRequest>(), CancellationToken.None))
            .ThrowsAsync(new ResourceNotFoundException("No key"));

        _classUnderTest = CreateClassUnderTest(CreateConfiguration(configValue));

        // Act
        var exception = Record.Exception(_classUnderTest.Load);

        // Assert
        Assert.IsType<KeyNotFoundException>(exception);
        Assert.Equal($"Secret '{CONNECTION_STRING_SECRET_KEY}' not found.", exception.Message);
    }

    [Fact]
    public void Load_ShouldThrowException_WhenGetSecretValueAsyncThrowsException()
    {
        // Arrange
        var configValue = new Dictionary<string, string?>
        {
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true",
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{LOAD_ON_STARTUP_FLAG_KEY}"] = "true"
        };

        const string ExceptionMessage = "Something Bad";
        _amazonSecretsManagerMock.Setup(x => x.GetSecretValueAsync(
                It.IsAny<GetSecretValueRequest>(), CancellationToken.None))
            .ThrowsAsync(new Exception(ExceptionMessage));

        _classUnderTest = CreateClassUnderTest(CreateConfiguration(configValue));

        // Act
        var exception = Record.Exception(_classUnderTest.Load);

        // Assert
        Assert.IsType<Exception>(exception);
        Assert.Equal(ExceptionMessage, exception.Message);
    }

    [Fact]
    public void Load_ShouldReturnEmpty_WhenGetSecretValueAsyncThrowsTaskCancelledException()
    {
        // Arrange
        var configValue = new Dictionary<string, string?>
        {
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_NAME_KEY}"] = CONNECTION_STRING_SECRET_KEY,
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{SECRETS_ROTATION_FLAG_KEY}"] = "true",
            [$"{SECRETS_SECTION_NAME}:{CONNECTION_STRING_SECRET_KEY}:{LOAD_ON_STARTUP_FLAG_KEY}"] = "true"
        };

        _amazonSecretsManagerMock.Setup(x => x.GetSecretValueAsync(
                It.IsAny<GetSecretValueRequest>(), CancellationToken.None))
            .ThrowsAsync(new TaskCanceledException("cancelled"));

        _classUnderTest = CreateClassUnderTest(CreateConfiguration(configValue));

        // Act
        _classUnderTest.Load();

        // Assert
        Assert.Empty(GetData());
    }

    private IDictionary<string, string?> GetData()
    {
        var propertyInfo = _classUnderTest
            .GetType().GetProperty("Data", BindingFlags.Instance | BindingFlags.NonPublic);

        var value = propertyInfo?.GetValue(_classUnderTest);

        return (IDictionary<string, string?>)value!;
    }

    private static IEnumerable<KeyValuePair<string, string?>> TransformValues(string configurationKey, string secretValue)
        => [new KeyValuePair<string, string?>(configurationKey, secretValue)];

    private static IConfiguration CreateConfiguration(IDictionary<string, string?> configuration)
        => new ConfigurationBuilder().AddInMemoryCollection(configuration).Build();

    private SecretsConfigurationProvider CreateClassUnderTest(IConfiguration configuration)
        => new(configuration, _amazonSecretsManagerMock.Object, TransformValues);
}
