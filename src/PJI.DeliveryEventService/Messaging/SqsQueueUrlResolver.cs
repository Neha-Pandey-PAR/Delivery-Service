using Amazon.SQS;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PJI.DeliveryEventService.Configuration;

namespace PJI.DeliveryEventService.Messaging;

/// <summary>
/// Resolves the persistence queue's URL from its name once at startup so that
/// both the API path (DeliveryEventQueue) and the dispatcher can read it from
/// SqsOptions without each having to do its own lookup. Runs before any other
/// hosted service that depends on SqsOptions.PersistenceQueueUrl.
/// </summary>
public class SqsQueueUrlResolver(
    IAmazonSQS sqsClient,
    IOptions<SqsOptions> sqsOptions,
    ILogger<SqsQueueUrlResolver> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = sqsOptions.Value;
        if (!string.IsNullOrEmpty(opts.PersistenceQueueUrl))
        {
            return;
        }

        if (string.IsNullOrEmpty(opts.PersistenceQueueName))
        {
            logger.LogWarning("Sqs:PersistenceQueueName is not configured; skipping queue URL resolution.");
            return;
        }

        var response = await sqsClient.GetQueueUrlAsync(opts.PersistenceQueueName, cancellationToken);
        opts.PersistenceQueueUrl = response.QueueUrl;
        logger.LogInformation("Resolved persistence queue URL {QueueUrl}", opts.PersistenceQueueUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
