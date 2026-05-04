using System.Diagnostics;

namespace PJI.DeliveryEventService.Infrastructure;

internal sealed class TraceparentPropagationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Activity.Current is { } activity && !request.Headers.Contains("traceparent"))
            request.Headers.TryAddWithoutValidation("traceparent", activity.Id);

        return base.SendAsync(request, cancellationToken);
    }
}
