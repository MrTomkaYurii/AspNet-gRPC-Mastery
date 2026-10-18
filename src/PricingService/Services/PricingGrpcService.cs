using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Mastery.Pricing.V1;
using Microsoft.EntityFrameworkCore;
using PricingService.Data;

namespace PricingService.Services;

/// <summary>
/// Реалізація gRPC-сервісу ціноутворення.
/// Демонструє Unary-розрахунки, Client Streaming (bulk upload) та Bi-directional Streaming (живі переговори).
/// </summary>
public class PricingGrpcService : Mastery.Pricing.V1.PricingService.PricingServiceBase
{
    private readonly PricingDbContext _dbContext;
    private readonly ILogger<PricingGrpcService> _logger;

    public PricingGrpcService(PricingDbContext dbContext, ILogger<PricingGrpcService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 1. Unary RPC: Розрахунок ціни з урахуванням грейду клієнта та обсягу партії.
    /// </summary>
    public override async Task<ProductPriceResponse> GetProductPrice(ProductPriceRequest request, ServerCallContext context)
    {
        _logger.LogInformation("--> [PricingService] GetProductPrice: SKU '{Sku}', Tier '{Tier}', Qty {Qty}",
            request.Sku, request.CustomerTier, request.RequestedQuantity);

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Артикул товару (SKU) не вказано."));
        }

        var priceEntity = await _dbContext.Prices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Sku == request.Sku, context.CancellationToken);

        if (priceEntity is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Цінову позицію для SKU '{request.Sku}' не знайдено."));
        }

        // Обчислення персональної знижки на основі грейду
        double tierDiscount = request.CustomerTier?.ToUpperInvariant() switch
        {
            "VIP" => 10.0,
            "GOLD" => 7.0,
            "SILVER" => 4.0,
            _ => 0.0
        };

        // Додаткова знижка за гуртову кількість
        double bulkDiscount = request.RequestedQuantity >= 10 ? 3.0 : 0.0;

        double totalDiscountPercent = Math.Min(30.0, priceEntity.StandardDiscountPercent + tierDiscount + bulkDiscount);
        double finalUnitPrice = Math.Round(priceEntity.BasePrice * (1.0 - totalDiscountPercent / 100.0), 2);

        string campaign = totalDiscountPercent > 0
            ? $"Tier: {request.CustomerTier ?? "Standard"} (+{tierDiscount}%), Bulk: (+{bulkDiscount}%)"
            : "Regular Base Price";

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
    /// 2. Client Streaming RPC: Клієнт надсилає потік правил знижок пачками,
    /// а сервер обробляє їх і після закриття потоку повертає єдиний звіт (BulkUploadSummary).
    /// </summary>
    public override async Task<BulkUploadSummary> UploadBulkDiscountRules(
        IAsyncStreamReader<DiscountRule> requestStream,
        ServerCallContext context)
    {
        _logger.LogInformation("--> [PricingService] UploadBulkDiscountRules: відкрито клієнтський потік оновлення правил...");

        int processed = 0;
        int applied = 0;
        int skipped = 0;

        // Зручний асинхронний обхід елементів клієнтського потоку
        await foreach (var rule in requestStream.ReadAllAsync(context.CancellationToken))
        {
            processed++;
            _logger.LogInformation("   [Stream Chunk Received] Правило для SKU: '{Sku}', Знижка: {Discount}%, Кампанія: '{Campaign}'",
                rule.Sku, rule.DiscountPercent, rule.CampaignName);

            var item = await _dbContext.Prices.FirstOrDefaultAsync(x => x.Sku == rule.Sku, context.CancellationToken);
            if (item is not null && rule.DiscountPercent >= 0 && rule.DiscountPercent <= 50)
            {
                item.StandardDiscountPercent = rule.DiscountPercent;
                item.UpdatedAt = DateTime.UtcNow;
                applied++;
            }
            else
            {
                skipped++;
            }
        }

        if (applied > 0)
        {
            await _dbContext.SaveChangesAsync(context.CancellationToken);
        }

        _logger.LogInformation("--> [PricingService] Клієнт закрив потік. Оброблено: {Processed}, Застосовано: {Applied}", processed, applied);

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
    /// 3. Bi-directional Streaming RPC: Двосторонній інтерактивний канал торгів.
    /// Клієнт та сервер читають і пишуть у потік одночасно та незалежно.
    /// </summary>
    public override async Task LiveQuoteNegotiation(
        IAsyncStreamReader<QuoteRequest> requestStream,
        IServerStreamWriter<QuoteResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("--> [PricingService] LiveQuoteNegotiation: встановлено двосторонній сеанс торгів.");

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
                // Бізнес-логіка торгів: якщо обсяг великий, даємо суттєву знижку
                double minAcceptablePrice = priceEntity.BasePrice * 0.82; // Мінімальна маржинальна ціна (-18%)

                if (quoteReq.TargetUnitBudget >= minAcceptablePrice)
                {
                    // Погоджуємось на пропозицію клієнта
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
                    // Контр-пропозиція сервера
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

            // Миттєво надсилаємо зустрічну відповідь у зворотному напрямку потоку
            await responseStream.WriteAsync(quoteResp, context.CancellationToken);
        }

        _logger.LogInformation("--> [PricingService] LiveQuoteNegotiation: двосторонній сеанс закрито.");
    }
}
