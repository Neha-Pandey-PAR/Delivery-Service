using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using PJI.DeliveryEventService.Authentication;
using PJI.DeliveryEventService.Handlers.DroppedOff;
using PJI.DeliveryEventService.Handlers.DroppedOff.Contracts;
using PJI.DeliveryEventService.Handlers.DroppedOff.Models;
using PJI.DeliveryEventService.Infrastructure;
using PJI.DeliveryEventService.Models.v1;
using PJI.DeliveryEventService.Resources;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;

namespace PJI.DeliveryEventService.Controllers.v1;

[ApiVersion("1")]
[Route("v{api-version:apiVersion}/orders")]
[ApiController]
[Consumes("application/json")]
[Produces("application/json")]
// [Authorize(AuthenticationSchemes = HmacAuthenticationDefaults.SchemeName)] // TEMP: disabled for local debugging - re-enable before commit
public class OrderEventsController(ProblemDetailsFactory problemDetailsFactory) : ControllerBase
{
    private readonly ProblemDetailsFactory _problemDetailsFactory = problemDetailsFactory;

    [HttpPost("{orderId}/events/dropped-off")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DroppedOff(
        [FromServices] IDroppedOffEventHandler handler,
        [Required][FromRoute] long orderId,
        [Required][FromBody] DroppedOffRequestBody body,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
            return CreateProblem(HttpStatusCode.BadRequest, ErrorCodes.ValidationError,
                ValidationMessages.OrderIdMustBePositive);

        if (body.IsInternal && body.EmployeeId is null)
            return CreateProblem(HttpStatusCode.BadRequest, ErrorCodes.ValidationError,
                ValidationMessages.EmployeeIdRequiredForInternalDelivery);

        var request = new DroppedOffEventRequest
        {
            OrderId = orderId,
            EventId = body.EventId,
            LocationNumber = body.LocationNumber,
            IsInternal = body.IsInternal,
            EmployeeId = body.EmployeeId,
            ClientName = User.FindFirstValue("client_name") ?? string.Empty,
            CorrelationId = HttpContext.GetCorrelationId()
        };

        var reply = await handler.HandleAsync(request, cancellationToken);

        if (reply.Success)
            return Accepted();

        return reply.ErrorCode switch
        {
            ErrorCodes.LocationNotFound => CreateProblem(HttpStatusCode.NotFound,
                ErrorCodes.LocationNotFound,
                ValidationMessages.LocationNotFoundDetail(body.LocationNumber)),
            _ => CreateProblem(HttpStatusCode.ServiceUnavailable,
                ErrorCodes.ServiceUnavailable,
                ValidationMessages.ServiceUnavailableTitle)
        };
    }

    private IActionResult CreateProblem(HttpStatusCode statusCode, string errorCode, string errorMessage)
    {
        var problemDetails = _problemDetailsFactory.CreateProblemDetails(HttpContext, statusCode: (int)statusCode);
        problemDetails.Extensions["errors"] = new ErrorResponse
        {
            Code = errorCode,
            Message = errorMessage
        };
        return StatusCode((int)statusCode, problemDetails);
    }
}
