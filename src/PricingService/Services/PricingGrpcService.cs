using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Mastery.Pricing.V1;
using Microsoft.EntityFrameworkCore;
using PricingService.Data;

namespace PricingService.Services;

/// <summary>
/// Серверна реалізація gRPC-сервісу динамічного ціноутворення та котирувань (PricingService).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// Цей сервіс демонструє три ключові моделі комунікації gRPC:
/// 1. Unary RPC ('GetProductPrice') — класичний запит-відповідь для швидкого розрахунку цін.
/// 2. Client Streaming RPC ('UploadBulkDiscountRules') — пакетне завантаження великих наборів даних від клієнта
///    до сервера з мінімальним споживанням оперативної пам'яті (пакетна обробка чанками).
/// 3. Bi-directional Streaming RPC ('LiveQuoteNegotiation') — повнодуплексний паралельний обмін повідомленнями
///    (живі торги/котирування), де клієнт і сервер надсилають повідомлення незалежно в межах єдиного HTTP/2-потоку.
/// </summary>
public class PricingGrpcService : Mastery.Pricing.V1.PricingService.PricingServiceBase
{
    private readonly PricingDbContext _dbContext;
    private readonly ILogger<PricingGrpcService> _logger;

    /// <summary>
    /// Конструктор сервісу ціноутворення.
    /// </summary>
    /// <param name="dbContext">Контекст бази даних SQLite цінових позицій (час життя Scoped).</param>
    /// <param name="logger">Діагностичний логер Kestrel/ASP.NET Core.</param>
    public PricingGrpcService(PricingDbContext dbContext, ILogger<PricingGrpcService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 1. UNARY RPC: Розрахунок підсумкової вартості товару з урахуванням клієнтського грейду та обсягу партії.
    /// Модель: 1 Request -> 1 Response.
    /// </summary>
    /// <param name="request">Запит з артикулом (SKU), грейдом клієнта (VIP/GOLD/SILVER) та кількістю.</param>
    /// <param name="context">Контекст виклику (CancellationToken, дедлайни, заголовки HTTP/2).</param>
    /// <returns>Деталізований розрахунок ціни з відсотком знижки та назвою застосованої кампанії.</returns>
    /// <exception cref="RpcException">
    /// - StatusCode.InvalidArgument: якщо артикул порожній.
    /// - StatusCode.NotFound: якщо цінова позиція для вказаного SKU відсутня.
    /// </exception>
    public override async Task<ProductPriceResponse> GetProductPrice(ProductPriceRequest request, ServerCallContext context)
    {
        _logger.LogInformation("--> [PricingService] GetProductPrice: SKU '{Sku}', Tier '{Tier}', Qty {Qty}",
            request.Sku, request.CustomerTier, request.RequestedQuantity);

        // КРОК 1: Валідація обов'язкових полів контракту
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Артикул товару (SKU) не вказано."));
        }

        // КРОК 2: Асинхронний пошук базової вартості в базі даних без накладних витрат трекінгу
        var priceEntity = await _dbContext.Prices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Sku == request.Sku, context.CancellationToken);

        if (priceEntity is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Цінову позицію для SKU '{request.Sku}' не знайдено."));
        }

        // КРОК 3: Розрахунок персональної знижки на основі лояльності клієнта (Customer Tier)
        double tierDiscount = request.CustomerTier?.ToUpperInvariant() switch
        {
            "VIP" => 10.0,
            "GOLD" => 7.0,
            "SILVER" => 4.0,
            _ => 0.0
        };

        // КРОК 4: Додатковий бонус за гуртову закупівлю (Bulk discount)
        double bulkDiscount = request.RequestedQuantity >= 10 ? 3.0 : 0.0;

        // КРОК 5: Калькуляція підсумкової ціни з обмеженням максимальної сумарної знижки (Cap: 30%)
        double totalDiscountPercent = Math.Min(30.0, priceEntity.StandardDiscountPercent + tierDiscount + bulkDiscount);
        double finalUnitPrice = Math.Round(priceEntity.BasePrice * (1.0 - totalDiscountPercent / 100.0), 2);

        string campaign = totalDiscountPercent > 0
            ? $"Tier: {request.CustomerTier ?? "Standard"} (+{tierDiscount}%), Bulk: (+{bulkDiscount}%)"
            : "Regular Base Price";

        // КРОК 6: Побудова Protobuf-відповіді з використанням стандартного типу Google.Protobuf.WellKnownTypes.Timestamp
        return new ProductPriceResponse
        {
            Sku = priceEntity.Sku,
            BaseUnitPrice = priceEntity.BasePrice,
            FinalUnitPrice = finalUnitPrice,
            DiscountPercentage = totalDiscountPercent,
            AppliedDiscountCampaign = campaign,
            Currency = priceEntity.Currency,
            CalculatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    /// <summary>
    /// 2. CLIENT STREAMING RPC: Пакетне завантаження та оновлення правил дисконтів.
    /// Модель: N Requests (Stream) -> 1 Response.
    /// 
    /// АРХІТЕКТУРНА СУТЬ CLIENT STREAMING:
    /// - Клієнт відкриває HTTP/2-потік і надсилає послідовність повідомлень 'DiscountRule' частинами (чанками).
    /// - Сервер не чекає завантаження всього масиву в оперативну пам'ять (як було б у REST multipart/json body).
    ///   Замість цього сервер обробляє елементи реактивно за допомогою асинхронного ітератора 'requestStream.ReadAllAsync()'.
    /// - Коли клієнт викликає 'RequestStream.CompleteAsync()', потік закривається, і сервер надсилає
    ///   фінальну підсумкову квитанцію 'BulkUploadSummary'.
    /// </summary>
    /// <param name="requestStream">Асинхронний рідер для читання вхідних повідомлень клієнта по мірі їх надходження.</param>
    /// <param name="context">Контекст gRPC-виклику з токеном скасування.</param>
    /// <returns>Агрегований звіт про кількість успішно оброблених, застосованих та пропущених записів.</returns>
    public override async Task<BulkUploadSummary> UploadBulkDiscountRules(
        IAsyncStreamReader<DiscountRule> requestStream,
        ServerCallContext context)
    {
        _logger.LogInformation("--> [PricingService] UploadBulkDiscountRules: відкрито клієнтський потік оновлення правил...");

        int processed = 0;
        int applied = 0;
        int skipped = 0;

        // Читання клієнтського потоку за допомогою C# 8+ 'await foreach' та розширення Grpc.Core 'ReadAllAsync'
        await foreach (var rule in requestStream.ReadAllAsync(context.CancellationToken))
        {
            processed++;
            _logger.LogInformation("   [Stream Chunk Received] Правило для SKU: '{Sku}', Знижка: {Discount}%, Кампанія: '{Campaign}'",
                rule.Sku, rule.DiscountPercent, rule.CampaignName);

            // Знаходимо позицію в базі даних та оновлюємо розмір знижки
            var item = await _dbContext.Prices.FirstOrDefaultAsync(x => x.Sku == rule.Sku, context.CancellationToken);
            if (item is not null && rule.DiscountPercent >= 0 && rule.DiscountPercent <= 50)
            {
                item.StandardDiscountPercent = rule.DiscountPercent;
                item.UpdatedAt = DateTime.UtcNow;
                applied++;
            }
            else
            {
                // Якщо товар не знайдено або відсоток виходить за допустимі межі (0-50%)
                skipped++;
            }
        }

        // Атомарна фіксація всіх валідних оновлень у межах однієї транзакції SQLite
        if (applied > 0)
        {
            await _dbContext.SaveChangesAsync(context.CancellationToken);
        }

        _logger.LogInformation("--> [PricingService] Клієнт закрив потік. Оброблено: {Processed}, Застосовано: {Applied}", processed, applied);

        // Повертаємо єдину відповідь клієнту після повного завершення потоку
        return new BulkUploadSummary
        {
            RulesProcessed = processed,
            RulesApplied = applied,
            RulesSkipped = skipped,
            StatusMessage = $"Пакетне оновлення завершено. Оновлено {applied} позицій.",
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    /// <summary>
    /// 3. BI-DIRECTIONAL STREAMING RPC: Інтерактивний повнодуплексний канал узгодження котирувань (Live Negotiation).
    /// Модель: N Requests (Stream) <---> M Responses (Stream).
    /// 
    /// АРХІТЕКТУРНА СУТЬ BI-DIRECTIONAL STREAMING:
    /// - Забезпечує незалежний двосторонній зв'язок поверх єдиного HTTP/2 TCP-з'єднання.
    /// - Сервер може надсилати зустрічні пропозиції одразу після отримання запиту абонента,
    ///   або навіть асинхронно ініціювати повідомлення у відповідь на внутрішні біржові події.
    /// - Жодна зі сторін не блокує іншу (Full-Duplex Multiplexing).
    /// </summary>
    /// <param name="requestStream">Вхідний потік цінових запитів від клієнта (бажаний бюджет, обсяг партії).</param>
    /// <param name="responseStream">Вихідний потік офіційних рішень та контр-пропозицій від сервера.</param>
    /// <param name="context">Контекст RPC-виклику.</param>
    public override async Task LiveQuoteNegotiation(
        IAsyncStreamReader<QuoteRequest> requestStream,
        IServerStreamWriter<QuoteResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("--> [PricingService] LiveQuoteNegotiation: встановлено двосторонній сеанс торгів.");

        // Безперервний цикл читання пропозицій клієнта
        await foreach (var quoteReq in requestStream.ReadAllAsync(context.CancellationToken))
        {
            _logger.LogInformation("   [Bi-Di Request] Сесія '{SessionId}': SKU '{Sku}', Партія {Volume} шт., Бажана ціна: ${Budget}",
                quoteReq.SessionId, quoteReq.Sku, quoteReq.BatchVolume, quoteReq.TargetUnitBudget);

            var priceEntity = await _dbContext.Prices
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Sku == quoteReq.Sku, context.CancellationToken);

            QuoteResponse quoteResp;

            if (priceEntity is null)
            {
                // Сценарій помилки: SKU не існує в прайс-листі
                quoteResp = new QuoteResponse
                {
                    SessionId = quoteReq.SessionId,
                    Sku = quoteReq.Sku,
                    OfferedUnitPrice = 0,
                    IsAccepted = false,
                    CounterOfferNotes = "SKU не знайдено в каталозі цін."
                };
            }
            else
            {
                // Алгоритм автоматичного котирування цін:
                // Мінімально допустима ціна для гуртових партій становить -18% від базової вартості
                double minAcceptablePrice = priceEntity.BasePrice * 0.82;

                if (quoteReq.TargetUnitBudget >= minAcceptablePrice)
                {
                    // Пропозицію клієнта погоджено: його бюджет покриває мінімальну рентабельність
                    quoteResp = new QuoteResponse
                    {
                        SessionId = quoteReq.SessionId,
                        Sku = quoteReq.Sku,
                        OfferedUnitPrice = quoteReq.TargetUnitBudget,
                        IsAccepted = true,
                        CounterOfferNotes = $"Пропозицію погоджено для партії {quoteReq.BatchVolume} шт.!"
                    };
                }
                else
                {
                    // Бюджет занизький: формуємо зустрічну контр-пропозицію з безпечною знижкою (-12%)
                    double counterOffer = Math.Round(priceEntity.BasePrice * 0.88, 2);
                    quoteResp = new QuoteResponse
                    {
                        SessionId = quoteReq.SessionId,
                        Sku = quoteReq.Sku,
                        OfferedUnitPrice = counterOffer,
                        IsAccepted = false,
                        CounterOfferNotes = $"Ціна ${quoteReq.TargetUnitBudget} нижча за собівартість. Пропонуємо компроміс: ${counterOffer}."
                    };
                }
            }

            // Миттєво записуємо відповідь у вихідний канал.
            // Клієнт отримує кожне повідомлення одразу, не чекаючи завершення всієї торгової сесії.
            await responseStream.WriteAsync(quoteResp, context.CancellationToken);
        }

        _logger.LogInformation("--> [PricingService] LiveQuoteNegotiation: клієнт завершив потік. Сеанс торгів закрито.");
    }
}
