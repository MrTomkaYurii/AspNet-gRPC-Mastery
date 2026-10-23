using Grpc.Core;
using Grpc.Net.Client;
using Mastery.Inventory.V1;
using Mastery.Pricing.V1;

namespace ClientApp.Scenarios;

/// <summary>
/// Навчальний сценарій 02: Всі три потокові режими роботи gRPC (Streaming Modes).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// HTTP/2 дозволяє передавати необмежену кількість двійкових повідомлень у межах одного відкритого TCP-стріму:
/// 1. Server Streaming (1 Request -> N Responses):
///    Клієнт відправляє єдиний фільтр, а сервер повертає потік повідомлень по мірі їх готовності
///    через 'AsyncServerStreamingCall<TResponse>.ResponseStream.ReadAllAsync()'.
/// 2. Client Streaming (N Requests -> 1 Response):
///    Клієнт пише потік об'єктів у 'AsyncClientStreamingCall<TRequest, TResponse>.RequestStream.WriteAsync()',
///    сигналізує про завершення викликом 'CompleteAsync()', і отримує єдину квитанцію 'ResponseAsync'.
/// 3. Bi-directional Streaming (N Requests <---> M Responses):
///    Повнодуплексний паралельний обмін: клієнт запускає фоновий 'Task.Run' для читання відповідей
///    та одночасно надсилає нові запити у вихідний потік.
/// </summary>
public static class StreamingScenario
{
    /// <summary>
    /// Демонструє послідовну роботу всіх трьох потокових моделей.
    /// </summary>
    /// <param name="inventoryUrl">Адреса InventoryService (Server Streaming).</param>
    /// <param name="pricingUrl">Адреса PricingService (Client & Bi-directional Streaming).</param>
    public static async Task RunAsync(string inventoryUrl, string pricingUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=======================================================");
        Console.WriteLine(" 🌊 СЦЕНАРІЙ 02: ВСІ РЕЖИМИ ПОТОКІВ (STREAMING MODES)");
        Console.WriteLine("=======================================================");
        Console.ResetColor();

        // -----------------------------------------------------------------------------------------
        // ЧАСТИНА 1: SERVER STREAMING (Серверний потік)
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[1] --- SERVER STREAMING (Отримання аудиторського потоку залишків) ---");
        Console.WriteLine("Клієнт відправляє 1 запит фільтра, сервер шле потік оновлень у реальному часі...");

        using var invChannel = GrpcChannel.ForAddress(inventoryUrl);
        var invClient = new InventoryService.InventoryServiceClient(invChannel);

        // Відкриття серверного стріму: Kestrel повертає дескриптор AsyncServerStreamingCall
        using var serverStreamCall = invClient.StreamStockUpdates(new StockAuditFilter
        {
            WarehouseCode = "WH-KYIV-01",
            MinQuantityThreshold = 0
        });

        int receivedChunks = 0;
        // Читання фреймів DATA по мірі надходження без накопичення всього списку в оперативній пам'яті
        await foreach (var item in serverStreamCall.ResponseStream.ReadAllAsync())
        {
            receivedChunks++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   [Чанк #{receivedChunks} від сервера] SKU: {item.Sku,-18} | Назва: {item.ProductName,-30} | Залишок: {item.AvailableQuantity,3} шт.");
            Console.ResetColor();
        }
        Console.WriteLine($"✅ Серверний потік успішно завершено. Отримано позицій: {receivedChunks}");

        // -----------------------------------------------------------------------------------------
        // ЧАСТИНА 2: CLIENT STREAMING (Клієнтський потік)
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[2] --- CLIENT STREAMING (Пакетне завантаження правил знижок від клієнта) ---");
        Console.WriteLine("Клієнт шле пачку правил у потік і викликає CompleteAsync()...");

        using var pricingChannel = GrpcChannel.ForAddress(pricingUrl);
        var pricingClient = new PricingService.PricingServiceClient(pricingChannel);

        // Відкриття клієнтського стріму: дескриптор AsyncClientStreamingCall
        using var clientStreamCall = pricingClient.UploadBulkDiscountRules();

        var rules = new[]
        {
            new DiscountRule { Sku = "LAPTOP-PRO-16", CampaignName = "Black Friday Deal", DiscountPercent = 12.5, MinimumBulkQuantity = 2 },
            new DiscountRule { Sku = "PHONE-ULTRA-24", CampaignName = "Weekend Promo", DiscountPercent = 8.0, MinimumBulkQuantity = 1 },
            new DiscountRule { Sku = "HEADPHONES-ANC", CampaignName = "Audio Fest", DiscountPercent = 15.0, MinimumBulkQuantity = 5 }
        };

        // Потокова відправка повідомлень по одному
        foreach (var rule in rules)
        {
            Console.WriteLine($"   [Відправка чанку клієнтом] SKU: {rule.Sku,-18} | Знижка: {rule.DiscountPercent,4}% | Акція: '{rule.CampaignName}'");
            await clientStreamCall.RequestStream.WriteAsync(rule);
            await Task.Delay(200); // штучна навчальна пауза для візуалізації потоковості
        }

        // Повідомляємо сервер про закриття вихідного потоку (відправка HTTP/2 END_STREAM прапорця)
        await clientStreamCall.RequestStream.CompleteAsync();

        // Очікуємо фінальне підсумкове повідомлення від сервера
        var summary = await clientStreamCall.ResponseAsync;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Сервер повернув підсумок обробки потоку:");
        Console.WriteLine($"   - Оброблено:   {summary.RulesProcessed} правил");
        Console.WriteLine($"   - Застосовано: {summary.RulesApplied}");
        Console.WriteLine($"   - Пропущено:   {summary.RulesSkipped}");
        Console.WriteLine($"   - Повідомлення:{summary.StatusMessage}");
        Console.ResetColor();

        // -----------------------------------------------------------------------------------------
        // ЧАСТИНА 3: BI-DIRECTIONAL STREAMING (Двосторонній дуплексний потік)
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[3] --- BI-DIRECTIONAL STREAMING (Живі інтерактивні переговори за ціну) ---");
        Console.WriteLine("Клієнт та сервер спілкуються одночасно та незалежно в повнодуплексному режимі...");

        using var bidiCall = pricingClient.LiveQuoteNegotiation();

        // Фонова задача для безперервного читання зустрічних контр-пропозицій від сервера
        var readTask = Task.Run(async () =>
        {
            await foreach (var quoteResp in bidiCall.ResponseStream.ReadAllAsync())
            {
                Console.ForegroundColor = quoteResp.IsAccepted ? ConsoleColor.Green : ConsoleColor.Yellow;
                Console.WriteLine($"   <-- [ВІДПОВІДЬ СЕРВЕРА] SKU: {quoteResp.Sku} | Запропоновано: ${quoteResp.OfferedUnitPrice:F2} | Узгоджено: {quoteResp.IsAccepted}");
                Console.WriteLine($"       Коментар: {quoteResp.CounterOfferNotes}");
                Console.ResetColor();
            }
        });

        // Основний потік: клієнт надсилає серію пропозицій різної цінової рентабельності
        var bids = new[]
        {
            new QuoteRequest { SessionId = "BID-01", Sku = "LAPTOP-PRO-16", BatchVolume = 10, TargetUnitBudget = 1800.00 }, // занизька ціна (-28%)
            new QuoteRequest { SessionId = "BID-02", Sku = "LAPTOP-PRO-16", BatchVolume = 25, TargetUnitBudget = 2100.00 }, // прийнятна гуртова ціна (-16%)
            new QuoteRequest { SessionId = "BID-03", Sku = "PHONE-ULTRA-24", BatchVolume = 50, TargetUnitBudget = 1100.00 }  // вигідна пропозиція
        };

        foreach (var bid in bids)
        {
            Console.WriteLine($"   --> [КЛІЄНТ ПРОПОНУЄ] Партія: {bid.BatchVolume} шт. '{bid.Sku}', Бюджет: ${bid.TargetUnitBudget}");
            await bidiCall.RequestStream.WriteAsync(bid);
            await Task.Delay(400); // інтервал між заявками на біржі
        }

        // Завершуємо запис у потік від імені клієнта
        await bidiCall.RequestStream.CompleteAsync();

        // Очікуємо, поки сервер надішле всі відповіді та завершить свій вихідний потік
        await readTask;

        Console.WriteLine("✅ Двосторонній сеанс зв'язку успішно завершено.");
    }
}
