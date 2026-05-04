using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Configuration;

[ExcludeFromCodeCoverage]
public class SqsOptions
{
    public string PersistenceQueueName { get; set; } = string.Empty;
    public int VisibilityTimeoutSeconds { get; set; }
    public string PersistenceQueueUrl { get; set; } = string.Empty;
}
