using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Mastery.Aggregator.V1;
using Mastery.Inventory.V1;
using Mastery.Pricing.V1;

namespace GatewayService.Services;

/// <summary>
/// Серверна реалізація композитного gRPC-сервісу агрегації замовлень (OrderAggregatorService).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// Цей сервіс реалізує патерн проєктування розподілених систем 'Scatter-Gather' (Розкидай-Збирай) та
/// виступає фасадом (Aggregator / Composite DTO) над двома незалежними бекенд-мікросервісами:
/// 1. InventoryService (Складські залишки та наявність на складах)
/// 2. PricingService (Динамічне ціноутворення, дисконтні програми та грейди лояльності)
/// 
/// КЛЮЧОВІ ПРИНЦИПИ МІКРОСЕРВІСНОЇ ІНЖЕНЕРІЇ, РЕАЛІЗОВАНІ ТУТ:
/// - Паралельні non-blocking gRPC-виклики через об'єкти 'AsyncUnaryCall' та їх властивість 'ResponseAsync'.
/// - Наскрізне прокидання дедлайнів (Deadline Propagation) для запобігання вичерпанню пулу потоків та висячим запитам.
/// - Стійкість до часткових відмов (Graceful Degradation): класифікація залежностей на критичні (Hard) та
///   некритичні (Soft) із застосуванням fallback-стратегій (резервна оцінка вартості).
/// </summary>
public class OrderAggregatorService : OrderAggregator.OrderAggregatorBase
{
    private readonly Mastery.Inventory.V1.InventoryService.InventoryServiceClient _inventoryClient;
    private readonly Mastery.Pricing.V1.PricingService.PricingServiceClient _pricingClient;
    private readonly ILogger<OrderAggregatorService> _logger;

    /// <summary>
    /// Конструктор сервісу-агрегатора.
    /// Клієнти gRPC реєструються у DI через 'services.AddGrpcClient<TClient>()' і мають скоуп 'Transient/Scoped',
    /// використовуючи спільний кешований HTTP/2 пул з'єднань 'SocketsHttpHandler'.
    /// </summary>
    /// <param name="inventoryClient">Типізований клієнт gRPC для комунікації з InventoryService.</param>
    /// <param name="pricingClient">Типізований клієнт gRPC для комунікації з PricingService.</param>
    /// <param name="logger">Діагностичний логер GatewayService.</param>
    public OrderAggregatorService(
        Mastery.Inventory.V1.InventoryService.InventoryServiceClient inventoryClient,
        Mastery.Pricing.V1.PricingService.PricingServiceClient pricingClient,
        ILogger<OrderAggregatorService> logger)
    {
        _inventoryClient = inventoryClient;
        _pricingClient = pricingClient;
        _logger = logger;
    }

    /// <summary>
    /// Збирає повне зведення про замовлення (OrderOverview) шляхом паралельного опитування сервісів складу та цін.
    /// </summary>
    /// <param name="request">Запит з артикулом SKU, грейдом покупця та необхідною кількістю товару.</param>
    /// <param name="context">Контекст вхідного RPC-виклику, що містить глобальний дедлайн і CancellationToken.</param>
    /// <returns>Єдиний композитний DTO-об'єкт 'OrderOverviewResponse'.</returns>
    /// <exception cref="RpcException">
    /// - StatusCode.InvalidArgument: якщо SKU товару порожній.
    /// - StatusCode.NotFound: якщо товар відсутній на складі (критична залежність).
    /// - StatusCode.Unavailable: якщо сервіс складу недоступний.
    /// </exception>
    public override async Task<OrderOverviewResponse> GetOrderOverview(
        OrderOverviewRequest request,
        ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("--> [OrderAggregator] Початок агрегації замовлення для SKU: '{Sku}', Кількість: {Qty}",
            request.Sku, request.Quantity);

        // КРОК 1: Базова валідація вхідного контракту
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Артикул SKU не може бути порожнім."));
        }

        int quantity = request.Quantity > 0 ? request.Quantity : 1;

        // КРОК 2: DEADLINE & CANCELLATION PROPAGATION (Наскрізне прокидання обмежень часу)
        // ВАЖЛИВО: У розподілених системах, якщо клієнт встановив Deadline у 2 секунди, агрегатор ПОВИНЕН
        // транслювати цей самий дедлайн усім викликам до бекенд-сервісів через CallOptions!
        // Якщо цього не зробити, сервіс складу може зависнути на 30 секунд, блокуючи ресурси агрегатора,
        // хоча початковий клієнт уже давно розірвав з'єднання за таймаутом.
        var callOptions = new CallOptions(
            deadline: context.Deadline,
            cancellationToken: context.CancellationToken);

        // КРОК 3: SCATTER-GATHER (Паралельне розсилання запитів)
        // Зверніть увагу: ми викликаємо 'Async' метод БЕЗ ключового слова 'await'!
        // Завдяки цьому HTTP/2 фрейми HEADERS і DATA відправляються в мережу негайно для обох мікросервісів,
        // а ми отримуємо об'єкти 'Task<T>' через властивість 'ResponseAsync'.
        // Час виконання обох викликів дорівнює MAX(t_inventory, t_pricing), а НЕ їх сумі (t_inventory + t_pricing)!
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

        // КРОК 4: Обробка результату критичної залежності (Hard Dependency: InventoryService)
        // Якщо товар фізично не зареєстрований на складі, формувати замовлення не має сенсу.
        // Тому помилка складу є фатальною для всієї агрегації і призводить до переривання операції.
        try
        {
            stock = await stockTask;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("Товар '{Sku}' не знайдено на складі при агрегації.", request.Sku);
            throw; // Транслюємо StatusCode.NotFound безпосередньо клієнту
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Помилка зв'язку зі складом при агрегації.");
            throw new RpcException(new Status(StatusCode.Unavailable, "Сервіс складу тимчасово недоступний."));
        }

        // КРОК 5: GRACEFUL DEGRADATION / FALLBACK (М'яка деградація для некритичної залежності)
        // PricingService розглядається як "Soft Dependency". Якщо сервіс розрахунку цін тимчасово ліг,
        // ми не повертаємо клієнту загальну помилку 503/Unavailable!
        // Замість цього ми перехоплюємо RpcException, активуємо fallback-калькуляцію з базовою ціною,
        // та встановлюємо прапорець 'IsPriceEstimated = true' з попередженням про орієнтовність розрахунку.
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
            
            // Формування тимчасового деградаційного цінового об'єкта (Default Safety Net)
            price = new ProductPriceResponse
            {
                Sku = request.Sku,
                BaseUnitPrice = 500.00,
                FinalUnitPrice = 500.00,
                DiscountPercentage = 0.0,
                AppliedDiscountCampaign = "Fallback Price Model (Degraded Mode)",
                Currency = "USD",
                CalculatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        stopwatch.Stop();

        // КРОК 6: Підсумкові бізнес-обчислення загальної суми замовлення
        double totalDiscount = Math.Round((price.BaseUnitPrice - price.FinalUnitPrice) * quantity, 2);
        double totalPrice = Math.Round(price.FinalUnitPrice * quantity, 2);

        _logger.LogInformation("<-- [OrderAggregator] Агрегацію завершено за {Elapsed} ms. Estimated: {IsEstimated}",
            stopwatch.ElapsedMilliseconds, isPriceEstimated);

        // КРОК 7: Компонування фінального агрегованого DTO 'OrderOverviewResponse'
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
