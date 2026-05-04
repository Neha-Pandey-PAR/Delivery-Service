using FluentAssertions;
using PJI.DeliveryEventService.Infrastructure;
using System.Diagnostics;

namespace PJI.DeliveryEventService.Tests.Infrastructure;

public class CorrelationIdProviderTests
{
    [Fact]
    public void GetOrCreate_ShouldReturnVersion7Guid_WhenNoActivityIsCurrent()
    {
        // Arrange
        Activity.Current = null;

        // Act
        var result = CorrelationIdProvider.GetOrCreate();

        // Assert
        result.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void GetOrCreate_ShouldReturnGuidFromActivityTraceId_WhenActivityIsCurrent()
    {
        // Arrange
        using var activity = new Activity("test").Start();

        // Act
        var result = CorrelationIdProvider.GetOrCreate();

        // Assert
        // 32-char hex traceId converts cleanly to a Guid
        var expectedTraceId = activity.TraceId.ToString();
        result.Should().Be(Guid.Parse(expectedTraceId));
    }

    [Fact]
    public void GetOrCreate_ShouldReturnUniqueValues_WhenCalledMultipleTimesWithoutActivity()
    {
        // Arrange
        Activity.Current = null;

        // Act
        var first = CorrelationIdProvider.GetOrCreate();
        var second = CorrelationIdProvider.GetOrCreate();

        // Assert
        first.Should().NotBe(second);
    }
}
