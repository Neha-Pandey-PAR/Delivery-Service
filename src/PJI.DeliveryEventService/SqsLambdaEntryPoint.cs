using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using PJI.DeliveryEventService.Dispatcher;
using PJI.DeliveryEventService.SecretsManager;
using System.Diagnostics.CodeAnalysis;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace PJI.DeliveryEventService;

/// <summary>
/// Entry point for the SQS-triggered Lambda function that processes delivery events
/// from the persistence queue and calls the Cloud API to close orders.
/// 
/// The SAM/CloudFormation template should reference this type via:
///   <c>PJI.DeliveryEventService::PJI.DeliveryEventService.SqsLambdaEntryPoint::FunctionHandlerAsync</c>
/// 
/// This Lambda uses partial batch failure reporting - messages that fail with a
/// retriable error are returned in the <see cref="SQSBatchResponse.BatchItemFailures"/>
/// list, causing SQS to make them visible again for retry. Messages that succeed
/// or fail with a non-retriable error are implicitly acknowledged and deleted.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Lambda hosting bootstrap.")]
public class SqsLambdaEntryPoint
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SqsLambdaEntryPoint> _logger;

    /// <summary>
    /// Default constructor used by AWS Lambda runtime. Bootstraps the DI container
    /// with all required services.
    /// </summary>
    public SqsLambdaEntryPoint()
    {
        var builder = Host.CreateApplicationBuilder();

        // Add secrets manager configuration
        builder.Configuration.AddSecretsManager(builder.Configuration);

        // Register all application services (processors, clients, options, etc.)
        builder.Services.AddSqsLambdaServices(builder.Configuration);

        var host = builder.Build();
        _serviceProvider = host.Services;
        _logger = _serviceProvider.GetRequiredService<ILogger<SqsLambdaEntryPoint>>();

        _logger.LogInformation("SQS Lambda entry point initialized");
    }

    /// <summary>
    /// Constructor for testing - allows injection of a pre-configured service provider.
    /// </summary>
    internal SqsLambdaEntryPoint(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger<SqsLambdaEntryPoint>>();
    }

    /// <summary>
    /// Lambda handler invoked by SQS event source mapping. Processes a batch of
    /// SQS messages and returns partial batch failures for retriable errors.
    /// </summary>
    /// <param name="sqsEvent">The SQS event containing one or more messages.</param>
    /// <param name="context">Lambda execution context.</param>
    /// <returns>
    /// A <see cref="SQSBatchResponse"/> containing message IDs that failed with
    /// retriable errors. Messages not in this list are considered successfully
    /// processed and will be deleted from the queue by SQS.
    /// </returns>
    public async Task<SQSBatchResponse> FunctionHandlerAsync(SQSEvent sqsEvent, ILambdaContext context)
    {
        _logger.LogInformation(
            "Processing SQS batch with {MessageCount} messages. RequestId={RequestId}",
            sqsEvent.Records?.Count ?? 0, context.AwsRequestId);

        if (sqsEvent.Records == null || sqsEvent.Records.Count == 0)
        {
            return new SQSBatchResponse { BatchItemFailures = [] };
        }

        // Create a scope for this Lambda invocation to get scoped services
        using var scope = _serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ISqsMessageHandler>();

        var batchItemFailures = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var record in sqsEvent.Records)
        {
            using var cts = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(5));

            var result = await handler.HandleMessageAsync(record, cts.Token);

            if (result == ProcessResult.Retriable)
            {
                // Return this message ID so SQS will make it visible again for retry
                batchItemFailures.Add(new SQSBatchResponse.BatchItemFailure
                {
                    ItemIdentifier = record.MessageId
                });
            }
            // Success, Discard, and NonRetriable all result in message deletion:
            // - Success: Message processed successfully
            // - Discard: Message is malformed/invalid, no point retrying
            // - NonRetriable: Permanent failure, will go to DLQ after maxReceiveCount
        }

        _logger.LogInformation(
            "Batch processing complete. Processed={Processed}, Failures={Failures}",
            sqsEvent.Records.Count, batchItemFailures.Count);

        return new SQSBatchResponse { BatchItemFailures = batchItemFailures };
    }
}
