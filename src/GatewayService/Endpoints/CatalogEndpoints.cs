using Grpc.Core;
using Mastery.Inventory.V1;
using Mastery.Pricing.V1;

namespace GatewayService.Endpoints;

/// <summary>
/// Реалізація гібридного фасаду (REST API to gRPC Bridge / Backend-For-Frontend).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// В сучасних корпоративних системах часто виникає дилема:
/// 1. Зовнішні клієнти (браузерні SPA на React/Vue, публічні 3rd-party розробники, мобільні додатки)
///    зазвичай очікують класичний REST API поверх JSON та HTTP/1.1 через простоту тестування через curl/Postman
///    та відсутність нативної підтримки довільних HTTP/2 фреймів у браузерному Fetch API (без grpc-web проксі на Envoy).
/// 2. Внутрішня комунікація між бекенд-мікросервісами (Service-to-Service) вимагає максимальної продуктивності,
///    двійкової серіалізації Protobuf, стиснення заголовків HPACK та мультиплексування HTTP/2.
/// 
/// Даний клас демонструє патерн API Gateway: Kestrel приймає зовнішній REST-запит '/api/catalog/products/{sku}',
/// робить швидкі внутрішні gRPC-виклики до InventoryService та PricingService, транслює статуси помилок
/// 'RpcException' у стандартні коди HTTP (400, 404, 502) і повертає зручний JSON.
/// </summary>
public static class CatalogEndpoints
{
    /// <summary>
    /// Реєструє маршрути Minimal API у конвеєрі обробки запитів ASP.NET Core.
    /// </summary>
    /// <param name="routes">Маршрутизатор кінцевих точок застосунку.</param>
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder routes)
    {
        // Групування маршрутів для OpenAPI / Swagger документації
        var group = routes.MapGroup("/api/catalog").WithTags("Catalog REST API");

        // GET /api/catalog/products/{sku}?tier=VIP&qty=5
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
                // КРОК 1: Внутрішній gRPC-виклик до InventoryService
                // Зауважте: клієнт gRPC автоматично виділяється з DI-контейнера ASP.NET Core.
                // Токен скасування 'ct' прив'язаний до життєвого циклу HTTP-запиту клієнта:
                // якщо користувач закриє вкладку браузера, Kestrel перерве операцію.
                var stockResponse = await inventoryClient.GetStockItemAsync(
                    new StockItemRequest { Sku = sku },
                    cancellationToken: ct);

                // КРОК 2: Внутрішній gRPC-виклик до PricingService
                var priceResponse = await pricingClient.GetProductPriceAsync(
                    new ProductPriceRequest
                    {
                        Sku = sku,
                        CustomerTier = tier ?? "Standard",
                        RequestedQuantity = qty ?? 1
                    },
                    cancellationToken: ct);

                // КРОК 3: Формування JSON-відповіді для зовнішнього REST-клієнта
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
            // КРОК 4: Грамотна трансляція gRPC-винятків (RpcException) у відповідні HTTP-статуси:
            // StatusCode.NotFound -> HTTP 404 Not Found
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return Results.NotFound(new { Error = ex.Status.Detail, StatusCode = 404 });
            }
            // StatusCode.InvalidArgument -> HTTP 400 Bad Request
            catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
            {
                return Results.BadRequest(new { Error = ex.Status.Detail, StatusCode = 400 });
            }
            // Будь-який внутрішній gRPC збій бекенду -> HTTP 502 Bad Gateway
            catch (RpcException ex)
            {
                return Results.Problem(
                    detail: $"Внутрішній gRPC збій бекенду: {ex.Status.Detail} ({ex.StatusCode})",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }
}
