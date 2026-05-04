using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;

namespace PJI.DeliveryEventService.Resources;

[ExcludeFromCodeCoverage(Justification = "Resource accessor.")]
internal static class ValidationMessages
{
    private static readonly ResourceManager ResourceManager =
        new("PJI.DeliveryEventService.Resources.ValidationMessages", typeof(ValidationMessages).Assembly);

    internal static string OrderIdMustBePositive => Get(nameof(OrderIdMustBePositive));
    internal static string EmployeeIdRequiredForInternalDelivery => Get(nameof(EmployeeIdRequiredForInternalDelivery));
    internal static string LocationNotFoundTitle => Get(nameof(LocationNotFoundTitle));
    internal static string LocationNotFoundDetail(int locationNumber) =>
        string.Format(CultureInfo.InvariantCulture, Get(nameof(LocationNotFoundDetail)), locationNumber);
    internal static string ServiceUnavailableTitle => Get(nameof(ServiceUnavailableTitle));
    internal static string UnexpectedErrorTitle => Get(nameof(UnexpectedErrorTitle));

    private static string Get(string name) =>
        ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
