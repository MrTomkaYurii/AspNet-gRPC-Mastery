# ==============================================================================
# Multi-stage Dockerfile для всіх сервісів gRPC-Complete-Mastery (.NET 10)
# ==============================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Копіювання файлів проєктів для кешування шарів NuGet restore
COPY ["src/Shared.Contracts/Shared.Contracts.csproj", "src/Shared.Contracts/"]
COPY ["src/InventoryService/InventoryService.csproj", "src/InventoryService/"]
COPY ["src/PricingService/PricingService.csproj", "src/PricingService/"]
COPY ["src/GatewayService/GatewayService.csproj", "src/GatewayService/"]
COPY ["src/ClientApp/ClientApp.csproj", "src/ClientApp/"]

RUN dotnet restore "src/GatewayService/GatewayService.csproj"
RUN dotnet restore "src/ClientApp/ClientApp.csproj"

# Копіювання всього вихідного коду та публікація
COPY src/ src/
RUN dotnet publish "src/InventoryService/InventoryService.csproj" -c Release -o /out/inventory
RUN dotnet publish "src/PricingService/PricingService.csproj" -c Release -o /out/pricing
RUN dotnet publish "src/GatewayService/GatewayService.csproj" -c Release -o /out/gateway
RUN dotnet publish "src/ClientApp/ClientApp.csproj" -c Release -o /out/client

# ------------------------------------------------------------------------------
# 1. InventoryService Runtime
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS inventory-service
WORKDIR /app
COPY --from=build /out/inventory .
ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001
ENTRYPOINT ["dotnet", "InventoryService.dll"]

# ------------------------------------------------------------------------------
# 2. PricingService Runtime
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS pricing-service
WORKDIR /app
COPY --from=build /out/pricing .
ENV ASPNETCORE_URLS=http://+:5002
EXPOSE 5002
ENTRYPOINT ["dotnet", "PricingService.dll"]

# ------------------------------------------------------------------------------
# 3. GatewayService Runtime
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS gateway-service
WORKDIR /app
COPY --from=build /out/gateway .
ENV Services__InventoryUrl=http://inventory:5001
ENV Services__PricingUrl=http://pricing:5002
EXPOSE 5000 5005
ENTRYPOINT ["dotnet", "GatewayService.dll"]

# ------------------------------------------------------------------------------
# 4. ClientApp Runtime
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS client-app
WORKDIR /app
COPY --from=build /out/client .
ENV INVENTORY_URL=http://inventory:5001
ENV PRICING_URL=http://pricing:5002
ENV GATEWAY_REST_URL=http://gateway:5000
ENV GATEWAY_GRPC_URL=http://gateway:5005
ENTRYPOINT ["dotnet", "ClientApp.dll", "--all"]
