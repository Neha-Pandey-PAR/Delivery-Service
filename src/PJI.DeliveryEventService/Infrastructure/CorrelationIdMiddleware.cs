using Serilog.Context;

namespace PJI.DeliveryEventService.Infrastructure;

internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "x-correlation-id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = CorrelationIdProvider.GetOrCreate();

        context.Items[CorrelationIdAccessor.HttpContextItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();

        // Push into Serilog's LogContext so every log within this request is enriched.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}

internal static class CorrelationIdAccessor
{
    internal const string HttpContextItemKey = "CorrelationId";

    /// <summary>
    /// Reads the correlation id stamped onto the request by <see cref="CorrelationIdMiddleware"/>.
    /// Falls back to generating a fresh id if the middleware didn't run (defensive — shouldn't happen for normal requests).
    /// </summary>
    internal static Guid GetCorrelationId(this HttpContext context)
        => context.Items[HttpContextItemKey] is Guid id ? id : CorrelationIdProvider.GetOrCreate();
}
