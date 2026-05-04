using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using Moq;
using PJI.DeliveryEventService.Controllers.v1;
using PJI.DeliveryEventService.Handlers.DroppedOff;
using PJI.DeliveryEventService.Handlers.DroppedOff.Contracts;
using PJI.DeliveryEventService.Handlers.DroppedOff.Models;
using PJI.DeliveryEventService.Infrastructure;
using PJI.DeliveryEventService.Models.v1;
using System.Security.Claims;

namespace PJI.DeliveryEventService.Tests.Controllers.v1;

public class OrderEventsControllerTests
{
    private readonly Mock<IDroppedOffEventHandler> _handlerMock = new(MockBehavior.Strict);
    private readonly ProblemDetailsFactory _problemDetailsFactory =
        new DefaultProblemDetailsFactory(Options.Create(new Microsoft.AspNetCore.Mvc.ApiBehaviorOptions()));

    private OrderEventsController CreateController(string clientName = "PJI")
    {
        var controller = new OrderEventsController(_problemDetailsFactory);
        var identity = new ClaimsIdentity([new Claim("client_name", clientName)], "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private static DroppedOffRequestBody CreateValidBody() => new()
    {
        EventId = "evt-1",
        LocationNumber = 12345,
        IsInternal = false
    };

    [Fact]
    public async Task DroppedOff_ShouldReturnAccepted_WhenHandlerReturnsSuccess()
    {
        // Arrange
        var controller = CreateController();
        _handlerMock.Setup(h => h.HandleAsync(It.IsAny<DroppedOffEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DroppedOffEventReply { Success = true });

        // Act
        var result = await controller.DroppedOff(_handlerMock.Object, 999L, CreateValidBody(), CancellationToken.None);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        _handlerMock.VerifyAll();
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldReturnProblemDetailsWithValidationError_WhenOrderIdIsZero()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.DroppedOff(_handlerMock.Object, 0L, CreateValidBody(), CancellationToken.None);

        // Assert
        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = status.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["errors"].Should().BeOfType<ErrorResponse>()
            .Which.Code.Should().Be(ErrorCodes.ValidationError);
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldReturnProblemDetailsWithValidationError_WhenOrderIdIsNegative()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.DroppedOff(_handlerMock.Object, -1L, CreateValidBody(), CancellationToken.None);

        // Assert
        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldReturnProblemDetailsWithValidationError_When1PDAndEmployeeIdMissing()
    {
        // Arrange
        var controller = CreateController();
        var body = new DroppedOffRequestBody
        {
            EventId = "evt-1",
            LocationNumber = 12345,
            IsInternal = true,
            EmployeeId = null
        };

        // Act
        var result = await controller.DroppedOff(_handlerMock.Object, 999L, body, CancellationToken.None);

        // Assert
        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = status.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["errors"].Should().BeOfType<ErrorResponse>()
            .Which.Code.Should().Be(ErrorCodes.ValidationError);
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldReturnAccepted_When1PDAndEmployeeIdProvided()
    {
        // Arrange
        var controller = CreateController();
        var body = new DroppedOffRequestBody
        {
            EventId = "evt-1",
            LocationNumber = 12345,
            IsInternal = true,
            EmployeeId = 42
        };
        _handlerMock.Setup(h => h.HandleAsync(It.IsAny<DroppedOffEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DroppedOffEventReply { Success = true });

        // Act
        var result = await controller.DroppedOff(_handlerMock.Object, 999L, body, CancellationToken.None);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        _handlerMock.VerifyAll();
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldReturnProblemDetailsWithLocationNotFound_WhenHandlerReturnsLocationNotFound()
    {
        // Arrange
        var controller = CreateController();
        _handlerMock.Setup(h => h.HandleAsync(It.IsAny<DroppedOffEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DroppedOffEventReply { Success = false, ErrorCode = ErrorCodes.LocationNotFound });

        // Act
        var result = await controller.DroppedOff(_handlerMock.Object, 999L, CreateValidBody(), CancellationToken.None);

        // Assert
        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var problem = status.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["errors"].Should().BeOfType<ErrorResponse>()
            .Which.Code.Should().Be(ErrorCodes.LocationNotFound);
        _handlerMock.VerifyAll();
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldReturnProblemDetailsWithServiceUnavailable_WhenHandlerReturnsUnknownError()
    {
        // Arrange
        var controller = CreateController();
        _handlerMock.Setup(h => h.HandleAsync(It.IsAny<DroppedOffEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DroppedOffEventReply { Success = false, ErrorCode = "UnknownError" });

        // Act
        var result = await controller.DroppedOff(_handlerMock.Object, 999L, CreateValidBody(), CancellationToken.None);

        // Assert
        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        var problem = status.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["errors"].Should().BeOfType<ErrorResponse>()
            .Which.Code.Should().Be(ErrorCodes.ServiceUnavailable);
        _handlerMock.VerifyAll();
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldPropagateClientNameFromUserClaims_ToHandlerRequest()
    {
        // Arrange
        var controller = CreateController(clientName: "Acme3PD");
        DroppedOffEventRequest? captured = null;
        _handlerMock.Setup(h => h.HandleAsync(It.IsAny<DroppedOffEventRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DroppedOffEventRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new DroppedOffEventReply { Success = true });

        // Act
        await controller.DroppedOff(_handlerMock.Object, 999L, CreateValidBody(), CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.ClientName.Should().Be("Acme3PD");
        _handlerMock.VerifyAll();
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldPropagateRequestFields_ToHandler()
    {
        // Arrange
        var controller = CreateController();
        var body = new DroppedOffRequestBody
        {
            EventId = "evt-xyz",
            LocationNumber = 7777,
            IsInternal = false,
            EmployeeId = null
        };
        DroppedOffEventRequest? captured = null;
        _handlerMock.Setup(h => h.HandleAsync(It.IsAny<DroppedOffEventRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DroppedOffEventRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new DroppedOffEventReply { Success = true });

        // Act
        await controller.DroppedOff(_handlerMock.Object, 555L, body, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.OrderId.Should().Be(555L);
        captured.EventId.Should().Be("evt-xyz");
        captured.LocationNumber.Should().Be(7777);
        captured.IsInternal.Should().BeFalse();
        captured.CorrelationId.Should().NotBe(Guid.Empty);
        _handlerMock.VerifyAll();
        _handlerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DroppedOff_ShouldUseEmptyClientName_WhenClaimMissing()
    {
        // Arrange — controller without the client_name claim
        var controller = new OrderEventsController(_problemDetailsFactory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };
        DroppedOffEventRequest? captured = null;
        _handlerMock.Setup(h => h.HandleAsync(It.IsAny<DroppedOffEventRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DroppedOffEventRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new DroppedOffEventReply { Success = true });

        // Act
        await controller.DroppedOff(_handlerMock.Object, 999L, CreateValidBody(), CancellationToken.None);

        // Assert
        captured!.ClientName.Should().BeEmpty();
        _handlerMock.VerifyAll();
        _handlerMock.VerifyNoOtherCalls();
    }
}
