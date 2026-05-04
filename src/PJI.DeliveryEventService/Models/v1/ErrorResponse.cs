using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Models.v1;

[ExcludeFromCodeCoverage]
public class ErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
