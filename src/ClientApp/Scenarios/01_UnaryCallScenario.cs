using Grpc.Core;
using Grpc.Net.Client;
using Mastery.Inventory.V1;

namespace ClientApp.Scenarios;

/// <summary>
/// Навчальний сценарій 01: Базова Unary-комунікація та коректна обробка gRPC-статусів (RpcException).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// 1. Unary RPC — це найпростіша модель взаємодії "клієнт-сервер" (Point-to-Point):
///    Клієнт надсилає рівно 1 Protobuf-повідомлення запиту -> Сервер повертає рівно 1 Protobuf-повідомлення відповіді.
/// 2. Канал 'GrpcChannel.ForAddress' керує пулом HTTP/2 з'єднань, мультиплексуванням та DNS-резолвінгом.
///    Він є важковаговим і потокобезпечним, тому в реальних застосунках створюється один раз як Singleton
///    або керується фабрикою 'AddGrpcClient'.
/// 3. У gRPC замість HTTP-кодів (200, 404, 500) використовується 16 стандартизованих статусів ('Grpc.Core.StatusCode').
///    Будь-яка серверна помилка призводить до викидання 'RpcException' на боці клієнта.
/// </summary>
public static class UnaryCallScenario
{
    /// <summary>
    /// Виконує покрокову демонстрацію Unary-викликів до InventoryService.
    /// </summary>
    /// <param name="inventoryServiceUrl">URL адреса Kestrel сервера InventoryService (наприклад, http://localhost:5001).</param>
    public static async Task RunAsync(string inventoryServiceUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=======================================================");
        Console.WriteLine(" 📘 СЦЕНАРІЙ 01: UNARY RPC & ОБРОБКА СТАТУС-КОДІВ");
        Console.WriteLine("=======================================================");
        Console.ResetColor();

        // Створення HTTP/2 каналу зв'язку.
        // За замовчуванням для http:// без TLS потрібен перемикач Http2UnencryptedSupport.
        using var channel = GrpcChannel.ForAddress(inventoryServiceUrl);
        var client = new InventoryService.InventoryServiceClient(channel);

        // -----------------------------------------------------------------------------------------
        // КРОК 1: Успішний Unary виклик — отримання інформації про товар за SKU
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[1] Відправка запиту GetStockItemAsync для 'LAPTOP-PRO-16'...");
        try
        {
            // Виклик клієнтського методу: під капотом генерується POST /mastery.inventory.v1.InventoryService/GetStockItem
            var response = await client.GetStockItemAsync(new StockItemRequest { Sku = "LAPTOP-PRO-16" });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ Отримано успішну відповідь StockItemResponse:");
            Console.WriteLine($"   - Назва товару: {response.ProductName}");
            Console.WriteLine($"   - Артикул SKU:  {response.Sku}");
            Console.WriteLine($"   - Доступно:     {response.AvailableQuantity} шт.");
            Console.WriteLine($"   - Склад:        {response.WarehouseCode}");
            Console.WriteLine($"   - Статус:       {response.Status}");
            Console.WriteLine($"   - Поповнено:    {response.LastRestockedAt.ToDateTime():dd.MM.yyyy HH:mm:ss} UTC");
            Console.ResetColor();
        }
        catch (RpcException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Неочікувана помилка: {ex.StatusCode} - {ex.Status.Detail}");
            Console.ResetColor();
        }

        // -----------------------------------------------------------------------------------------
        // КРОК 2: Успішне транзакційне резервування залишку на складі
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[2] Відправка запиту ReserveStockAsync (резервування 2 шт.)...");
        try
        {
            var reserveResponse = await client.ReserveStockAsync(new ReserveStockRequest
            {
                Sku = "LAPTOP-PRO-16",
                QuantityToReserve = 2,
                OrderId = "ORD-2026-DEMO-001"
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ Резервування залишку успішне:");
            Console.WriteLine($"   - Статус:     {reserveResponse.Message}");
            Console.WriteLine($"   - Зарезерв.:  {reserveResponse.ReservedQuantity} шт.");
            Console.WriteLine($"   - Дійсний до: {reserveResponse.ReservationExpiresAt.ToDateTime():dd.MM.yyyy HH:mm:ss} UTC");
            Console.ResetColor();
        }
        catch (RpcException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Помилка: {ex.StatusCode} - {ex.Status.Detail}");
            Console.ResetColor();
        }

        // -----------------------------------------------------------------------------------------
        // КРОК 3: Обробка бізнес-помилки порушення правил валідації (StatusCode.FailedPrecondition)
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[3] Тест бізнес-помилки: Спроба зарезервувати 9999 шт. (більше, ніж є на складі)...");
        try
        {
            await client.ReserveStockAsync(new ReserveStockRequest
            {
                Sku = "LAPTOP-PRO-16",
                QuantityToReserve = 9999,
                OrderId = "ORD-FAIL-TEST"
            });
        }
        catch (RpcException ex)
        {
            // Очікуємо перехоплення помилки статусу FailedPrecondition (аналог HTTP 422 Unprocessable Entity)
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ Перехоплено очікувану RpcException:");
            Console.WriteLine($"   - StatusCode: {ex.StatusCode}");
            Console.WriteLine($"   - Detail:     {ex.Status.Detail}");
            Console.ResetColor();
        }

        // -----------------------------------------------------------------------------------------
        // КРОК 4: Обробка відсутності сутності в БД (StatusCode.NotFound)
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[4] Тест NotFound: Пошук неіснуючого товару 'NON-EXISTING-SKU-999'...");
        try
        {
            await client.GetStockItemAsync(new StockItemRequest { Sku = "NON-EXISTING-SKU-999" });
        }
        catch (RpcException ex)
        {
            // Очікуємо StatusCode.NotFound (аналог HTTP 404 Not Found)
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ Перехоплено очікувану RpcException:");
            Console.WriteLine($"   - StatusCode: {ex.StatusCode}");
            Console.WriteLine($"   - Detail:     {ex.Status.Detail}");
            Console.ResetColor();
        }
    }
}
