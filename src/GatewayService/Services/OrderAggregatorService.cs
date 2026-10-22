using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Mastery.Aggregator.V1;
using Mastery.Inventory.V1;
using Mastery.Pricing.V1;

namespace GatewayService.Services;

/// <summary>
/// Реалізація патерну Aggregator (Scatter-Gather / Composite DTO):
/// Одночасно опитує InventoryService та PricingService через gRPC,
/// обробляє часткові відмови (Graceful Degradation), прокидає Deadlines
/// та збирає єдину композитну відповідь.
/// </summary>
public class OrderAggregatorService : OrderAggregator.OrderAggregatorBase
{
    private readonly Mastery.Inventory.V1.InventoryService.InventoryServiceClient _inventoryClient;
    private readonly Mastery.Pricing.V1.PricingService.PricingServiceClient _pricingClient;
    private readonly ILogger<OrderAggregatorService> _logger;

    public OrderAggregatorService(
        Mastery.Inventory.V1.InventoryService.InventoryServiceClient inventoryClient,
        Mastery.Pricing.V1.PricingService.PricingServiceClient pricingClient,
        ILogger<OrderAggregatorService> logger)
    {
        _inventoryClient = inventoryClient;
        _pricingClient = pricingClient;
        _logger = logger;
    }

    public override async Task<OrderOverviewResponse> GetOrderOverview(
        OrderOverviewRequest request,
        ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("--> [OrderAggregator] Початок агрегації замовлення для SKU: '{Sku}', Кількість: {Qty}",
            request.Sku, request.Quantity);

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Артикул SKU не може бути порожнім."));
        }

        int quantity = request.Quantity > 0 ? request.Quantity : 1;

        // 1. Deadline Propagation: прокидаємо крайній термін із вхідного контексту
        var callOptions = new CallOptions(
            deadline: context.Deadline,
            cancellationToken: context.CancellationToken);

        // 2. Scatter-Gather: паралельний запуск викликів до двох мікросервісів
        var stockTask = _inventoryClient.GetStockItemAsync(
            new StockItemRequest { Sku = request.Sku },
            callOptions).ResponseAsync;

        var priceTask = _pricingClient.GetProductPriceAsync(
            new ProductPriceRequest
            {
                Sku = request.Sku,
                CustomerTier = request.CustomerTier ?? "Standard",
                RequestedQuantity = quantity
            },
            callOptions).ResponseAsync;

        StockItemResponse? stock = null;
        ProductPriceResponse? price = null;
        bool isPriceEstimated = false;
        string degradationWarning = string.Empty;

        // Очікуємо завершення складу (критична залежність)
        try
        {
            stock = await stockTask;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("Товар '{Sku}' не знайдено на складі при агрегації.", request.Sku);
            throw; // Якщо товару взагалі не існує в каталозі складу, агрегація неможлива
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Помилка зв'язку зі складом при агрегації.");
            throw new RpcException(new Status(StatusCode.Unavailable, "Сервіс складу недоступний."));
        }

        // 3. Graceful Degradation / Fallback для сервісу ціноутворення (некритична часткова відмова)
        try
        {
            price = await priceTask;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning("Помилка від PricingService ({StatusCode}: {Detail}). Застосовується fallback-оцінка.",
                ex.StatusCode, ex.Status.Detail);

            isPriceEstimated = true;
            degradationWarning = $"Сервіс цін тимчасово недоступний ({ex.StatusCode}). Розраховано базову орієнтовну вартість.";
            
            // Формуємо тимчасову резервну ціну
            price = new ProductPriceResponse
            {
                Sku = request.Sku,
                BaseUnitPrice = 500.00,
                FinalUnitPrice = 500.00,
                DiscountPercentage = 0.0,
                AppliedDiscountCampaign = "Fallback Price Model",
                Currency = "USD",
                CalculatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        stopwatch.Stop();

        double totalDiscount = Math.Round((price.BaseUnitPrice - price.FinalUnitPrice) * quantity, 2);
        double totalPrice = Math.Round(price.FinalUnitPrice * quantity, 2);

        _logger.LogInformation("<-- [OrderAggregator] Агрегацію завершено за {Elapsed} ms. Estimated: {IsEstimated}",
            stopwatch.ElapsedMilliseconds, isPriceEstimated);

        // 4. Формування єдиної Composite DTO
        return new OrderOverviewResponse
        {
            Sku = stock.Sku,
            ProductName = stock.ProductName,
            StockStatus = stock.Status,
            AvailableStock = stock.AvailableQuantity,
            PrimaryWarehouse = stock.WarehouseCode,
            UnitPrice = price.FinalUnitPrice,
            TotalPrice = totalPrice,
            TotalDiscountApplied = totalDiscount,
            Currency = price.Currency,
            IsPriceEstimated = isPriceEstimated,
            DegradationWarning = degradationWarning,
            AggregationLatencyMs = stopwatch.ElapsedMilliseconds,
            GeneratedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }
}
