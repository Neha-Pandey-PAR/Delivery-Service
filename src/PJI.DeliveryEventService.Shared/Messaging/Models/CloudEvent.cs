using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PJI.DeliveryEventService.Messaging.Models;

/// <summary>
/// CloudEvents v1.0 envelope for messages in the persistence queue.
/// See: https://cloudevents.io/
/// </summary>
[ExcludeFromCodeCoverage]
public class CloudEvent
{
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; set; } = "1.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("time")]
    public DateTimeOffset? Time { get; set; }

    [JsonPropertyName("datacontenttype")]
    public string? DataContentType { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}
