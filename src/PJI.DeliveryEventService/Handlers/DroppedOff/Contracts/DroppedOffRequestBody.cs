using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService.Handlers.DroppedOff.Contracts;

[ExcludeFromCodeCoverage]
public class DroppedOffRequestBody
{
    [Required] public string EventId { get; set; } = string.Empty;
    [Required] public int LocationNumber { get; set; }
    [Required] public bool IsInternal { get; set; }
    public int? EmployeeId { get; set; }
}
