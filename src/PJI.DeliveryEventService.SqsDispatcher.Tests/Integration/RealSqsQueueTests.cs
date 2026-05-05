using Amazon.Lambda.SQSEvents;
using Amazon.Runtime.CredentialManagement;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PJI.DeliveryEventService.SqsDispatcher.CloudApiClient;
using PJI.DeliveryEventService.SqsDispatcher.CloudApiClient.Models;
using PJI.DeliveryEventService.SqsDispatcher.Dispatcher;

namespace PJI.DeliveryEventService.SqsDispatcher.Tests.Integration;

/// <summary>
/// Integration tests that read from a real SQS queue.
/// 
/// Configure appsettings.Integration.json with your queue URL and credentials.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class RealSqsQueueTests : IAsyncLifetime
{
    private IHost? _host;
    private IAmazonSQS? _sqsClient;
    private ISqsMessageHandler? _handler;
    private string? _queueUrl;
    private bool _deleteAfterProcessing;

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Integration.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        _queueUrl = config["Sqs:QueueUrl"];
        _deleteAfterProcessing = config.GetValue("Sqs:DeleteAfterProcessing", false);

        if (string.IsNullOrEmpty(_queueUrl))
        {
            return; // Skip if not configured
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddConfiguration(config);

        // Register services
        builder.Services.AddSqsDispatcherServices(builder.Configuration);
        
        // Use mock Cloud API client for testing
        builder.Services.AddSingleton<ICloudApiOrderServiceClient, MockCloudApiClient>();
        
        // Add SQS client with profile credentials
        var profileName = config["AWS:Profile"] ?? "default";
        var region = Amazon.RegionEndpoint.GetBySystemName(config["AWS:Region"] ?? "us-east-1");
        
        var chain = new CredentialProfileStoreChain();
        if (chain.TryGetAWSCredentials(profileName, out var credentials))
        {
            builder.Services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(credentials, region));
        }
        else
        {
            // Fallback to default credential resolution
            builder.Services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(region));
        }

        _host = builder.Build();
        _sqsClient = _host.Services.GetRequiredService<IAmazonSQS>();
        _handler = _host.Services.GetRequiredService<ISqsMessageHandler>();

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ReadAndProcessMessage_FromRealQueue_ShouldSucceed()
    {
        // Skip if not configured
        if (string.IsNullOrEmpty(_queueUrl))
        {
            Assert.Fail("Sqs:QueueUrl not configured. Set it in appsettings.Integration.json or environment variables.");
            return;
        }

        // Arrange
        var receiveRequest = new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 5, // Short wait for testing
            AttributeNames = ["All"],
            MessageAttributeNames = ["All"]
        };

        // Act
        var response = await _sqsClient!.ReceiveMessageAsync(receiveRequest);

        if (response.Messages.Count == 0)
        {
            Assert.Fail("No messages in queue. Send a message to the queue first.");
            return;
        }

        var sqsMessage = response.Messages[0];
        Console.WriteLine($"Received MessageId: {sqsMessage.MessageId}");
        Console.WriteLine($"Body: {sqsMessage.Body}");

        // Convert to Lambda format
        var record = new SQSEvent.SQSMessage
        {
            MessageId = sqsMessage.MessageId,
            ReceiptHandle = sqsMessage.ReceiptHandle,
            Body = sqsMessage.Body,
            Attributes = sqsMessage.Attributes ?? new Dictionary<string, string>(),
            MessageAttributes = sqsMessage.MessageAttributes?.ToDictionary(
                kvp => kvp.Key,
                kvp => new SQSEvent.MessageAttribute
                {
                    StringValue = kvp.Value.StringValue,
                    DataType = kvp.Value.DataType
                }) ?? new Dictionary<string, SQSEvent.MessageAttribute>(),
            EventSource = "aws:sqs",
            AwsRegion = "us-east-1"
        };

        // Process
        var result = await _handler!.HandleMessageAsync(record, CancellationToken.None);

        // Assert
        Console.WriteLine($"Result: {result}");
        result.Should().BeOneOf(ProcessResult.Success, ProcessResult.Discard);

        // Optionally delete
        if (_deleteAfterProcessing && result == ProcessResult.Success)
        {
            await _sqsClient.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle);
            Console.WriteLine("Message deleted from queue");
        }
    }

    [Fact]
    public async Task PollAndProcessMessages_FromRealQueue_UntilEmpty()
    {
        // Skip if not configured
        if (string.IsNullOrEmpty(_queueUrl))
        {
            Assert.Fail("Sqs:QueueUrl not configured.");
            return;
        }

        var processedCount = 0;
        var maxIterations = 10; // Safety limit

        for (var i = 0; i < maxIterations; i++)
        {
            var receiveRequest = new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 2,
                AttributeNames = ["All"]
            };

            var response = await _sqsClient!.ReceiveMessageAsync(receiveRequest);

            if (response.Messages.Count == 0)
            {
                Console.WriteLine($"Queue empty after processing {processedCount} messages");
                break;
            }

            foreach (var sqsMessage in response.Messages)
            {
                var record = new SQSEvent.SQSMessage
                {
                    MessageId = sqsMessage.MessageId,
                    ReceiptHandle = sqsMessage.ReceiptHandle,
                    Body = sqsMessage.Body,
                    Attributes = sqsMessage.Attributes ?? new Dictionary<string, string>(),
                    EventSource = "aws:sqs"
                };

                var result = await _handler!.HandleMessageAsync(record, CancellationToken.None);
                Console.WriteLine($"Message {sqsMessage.MessageId}: {result}");

                if (_deleteAfterProcessing && result == ProcessResult.Success)
                {
                    await _sqsClient.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle);
                }

                processedCount++;
            }
        }

        Console.WriteLine($"Total processed: {processedCount}");
        processedCount.Should().BeGreaterThan(0, "Expected at least one message in queue");
    }

    private class MockCloudApiClient : ICloudApiOrderServiceClient
    {
        public Task<CloseOrderResult> CloseOrderAsync(long orderId, string accessToken, string locationToken, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[MOCK] CloseOrderAsync: orderId={orderId}");
            return Task.FromResult(CloseOrderResult.Success);
        }
    }
}
