using PJI.DeliveryEventService.CloudApiClient.Models;
using System.Globalization;

namespace PJI.DeliveryEventService.CloudApiClient;

public class CloudApiOrderServiceClient(HttpClient httpClient, ILogger<CloudApiOrderServiceClient> logger)
    : ICloudApiOrderServiceClient
{
    private const string CloseOrderPath = "v1/orders/{0}/close";

    public Task<CloseOrderResult> CloseOrderAsync(
        long orderId, string accessToken, string locationToken,
        CancellationToken cancellationToken)
    {
        var path = string.Format(CultureInfo.InvariantCulture, CloseOrderPath, orderId);
        return ExecuteAsync(
            BuildRequest(HttpMethod.Post, path, accessToken, locationToken),
            orderId, cancellationToken);
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method, string path,
        string accessToken, string locationToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("parpos-access-token", accessToken);
        request.Headers.TryAddWithoutValidation("parpos-location-token", locationToken);
        return request;
    }

    private async Task<CloseOrderResult> ExecuteAsync(
        HttpRequestMessage request, long orderId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return CloseOrderResult.Success;
            }

            var responseBody = await SafeReadResponseBodyAsync(response, cancellationToken);

            if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
            {
                logger.LogWarning(
                    "Non-retriable {StatusCode} for orderId={OrderId}. Response body: {ResponseBody}",
                    (int)response.StatusCode, orderId, responseBody);
                return CloseOrderResult.NonRetriable;
            }

            logger.LogWarning(
                "Retriable {StatusCode} for orderId={OrderId}. Response body: {ResponseBody}",
                (int)response.StatusCode, orderId, responseBody);
            return CloseOrderResult.Retriable;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Retriable HTTP error for orderId={OrderId}", orderId);
            return CloseOrderResult.Retriable;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Retriable Timeout for orderId={OrderId}", orderId);
            return CloseOrderResult.Retriable;
        }
    }

    private static async Task<string> SafeReadResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return "<unreadable response body>";
        }
    }
}
