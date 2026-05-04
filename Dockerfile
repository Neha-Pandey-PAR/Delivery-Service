# =============================================================================
# Dockerfile for PJI Delivery Event Service - API Lambda
#
# Multi-stage build:
#   1. SDK stage publishes the ASP.NET Core project.
#   2. Runtime stage uses the AWS Lambda .NET base image whose ENTRYPOINT
#      is the .NET Lambda runtime client. The handler to invoke is supplied
#      via CMD here (and may be overridden by SAM's ImageConfig.Command).
# =============================================================================

# ----- Build stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy central package management + project files first for layer caching.
COPY Directory.Packages.props ./
COPY NuGet.config* ./
COPY src/PJI.DeliveryEventService/PJI.DeliveryEventService.csproj src/PJI.DeliveryEventService/
COPY src/PJI.DeliveryEventService.SecretsManager/PJI.DeliveryEventService.SecretsManager.csproj src/PJI.DeliveryEventService.SecretsManager/

RUN dotnet restore src/PJI.DeliveryEventService/PJI.DeliveryEventService.csproj

# Copy source and publish the Lambda artifact.
COPY src/PJI.DeliveryEventService/ src/PJI.DeliveryEventService/
COPY src/PJI.DeliveryEventService.SecretsManager/ src/PJI.DeliveryEventService.SecretsManager/

RUN dotnet publish src/PJI.DeliveryEventService/PJI.DeliveryEventService.csproj \
    -c Release \
    -o /var/task \
    --no-restore

# ----- Runtime stage ---------------------------------------------------------
# AWS Lambda .NET base image. Tag must match the project's TargetFramework.
# NOTE: AWS may not yet publish a `dotnet:10` tag - if the image is missing,
# fall back to `provided.al2023` with a custom bootstrap script.
FROM public.ecr.aws/lambda/dotnet:10
WORKDIR /var/task
COPY --from=build /var/task/ ./

# Lambda handler: <AssemblyName>::<TypeName>::<MethodName>
CMD ["PJI.DeliveryEventService::PJI.DeliveryEventService.LambdaEntryPoint::FunctionHandlerAsync"]
