using Amazon.Lambda.AspNetCoreServer;
using PJI.DeliveryEventService.SecretsManager;
using System.Diagnostics.CodeAnalysis;

namespace PJI.DeliveryEventService;

/// <summary>
/// Entry point for hosting the ASP.NET Core API behind AWS API Gateway
/// (REST API / Lambda Proxy integration).
///
/// The SAM/CloudFormation template should reference this type via:
///   <c>PJI.DeliveryEventService::PJI.DeliveryEventService.LambdaEntryPoint::FunctionHandlerAsync</c>
///
/// For HTTP API (v2) integrations, switch the base class to
/// <see cref="APIGatewayHttpApiV2ProxyFunction"/>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Lambda hosting bootstrap.")]
public class LambdaEntryPoint : APIGatewayProxyFunction
{
    protected override void Init(IWebHostBuilder builder)
    {
        builder
            .ConfigureAppConfiguration((ctx, config) =>
                config.AddSecretsManager(ctx.Configuration))
            .UseStartup<Startup>();
    }

    protected override void Init(IHostBuilder builder)
    {
        // No host-level configuration required; Startup wires everything up
        // via ConfigureServices/Configure.
    }
}
