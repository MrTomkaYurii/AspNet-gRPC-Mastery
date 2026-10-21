using Grpc.Core;
using Mastery.Inventory.V1;
using Mastery.Pricing.V1;

namespace GatewayService.Endpoints;

/// <summary>
/// Демонстрація гібридної архітектури:
/// REST Minimal API приймає HTTP/JSON запит від веб-клієнта / браузера,
/// а під капотом звертається до бекенд-мікросервісів через високошвидкісний gRPC.
/// </summary>
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/catalog").WithTags("Catalog REST API");

        group.MapGet("/products/{sku}", async (
            string sku,
            string? tier,
            int? qty,
            Mastery.Inventory.V1.InventoryService.InventoryServiceClient inventoryClient,
            Mastery.Pricing.V1.PricingService.PricingServiceClient pricingClient,
            CancellationToken ct) =>
        {
            try
            {
                // Виклик gRPC мікросервісу складу
                var stockResponse = await inventoryClient.GetStockItemAsync(
                    new StockItemRequest { Sku = sku },
                    cancellationToken: ct);

                // Виклик gRPC мікросервісу цін
                var priceResponse = await pricingClient.GetProductPriceAsync(
                    new ProductPriceRequest
                    {
                        Sku = sku,
                        CustomerTier = tier ?? "Standard",
                        RequestedQuantity = qty ?? 1
                    },
                    cancellationToken: ct);

                // Повертаємо класичний JSON об'єкт для зовнішніх споживачів (SPA, мобільні додатки)
                var result = new
                {
                    Source = "Hybrid REST-over-gRPC Gateway",
                    Sku = stockResponse.Sku,
                    ProductName = stockResponse.ProductName,
                    Warehouse = stockResponse.WarehouseCode,
                    StockStatus = stockResponse.Status.ToString(),
                    AvailableUnits = stockResponse.AvailableQuantity,
                    Pricing = new
                    {
                        BasePrice = priceResponse.BaseUnitPrice,
                        FinalUnitPrice = priceResponse.FinalUnitPrice,
                        DiscountPercent = priceResponse.DiscountPercentage,
                        Campaign = priceResponse.AppliedDiscountCampaign,
                        Currency = priceResponse.Currency,
                        CalculatedTotal = Math.Round(priceResponse.FinalUnitPrice * (qty ?? 1), 2)
                    }
                };

                return Results.Ok(result);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return Results.NotFound(new { Error = ex.Status.Detail, StatusCode = 404 });
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
            {
                return Results.BadRequest(new { Error = ex.Status.Detail, StatusCode = 400 });
            }
            catch (RpcException ex)
            {
                return Results.Problem(
                    detail: $"Внутрішній gRPC збій: {ex.Status.Detail} ({ex.StatusCode})",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }
}
