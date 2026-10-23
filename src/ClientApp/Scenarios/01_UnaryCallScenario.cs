using Grpc.Core;
using Grpc.Net.Client;
using Mastery.Inventory.V1;

namespace ClientApp.Scenarios;

public static class UnaryCallScenario
{
    public static async Task RunAsync(string inventoryServiceUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=======================================================");
        Console.WriteLine(" 📘 СЦЕНАРІЙ 01: UNARY RPC & ОБРОБКА СТАТУС-КОДІВ");
        Console.WriteLine("=======================================================");
        Console.ResetColor();

        using var channel = GrpcChannel.ForAddress(inventoryServiceUrl);
        var client = new InventoryService.InventoryServiceClient(channel);

        // 1. Успішний Unary виклик: Отримання позиції за SKU
        Console.WriteLine("\n[1] Відправка запиту GetStockItemAsync для 'LAPTOP-PRO-16'...");
        try
        {
            var response = await client.GetStockItemAsync(new StockItemRequest { Sku = "LAPTOP-PRO-16" });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ Отримано відповідь StockItemResponse:");
            Console.WriteLine($"   - Назва:      {response.ProductName}");
            Console.WriteLine($"   - SKU:        {response.Sku}");
            Console.WriteLine($"   - Доступно:   {response.AvailableQuantity} шт.");
            Console.WriteLine($"   - Склад:      {response.WarehouseCode}");
            Console.WriteLine($"   - Статус:     {response.Status}");
            Console.WriteLine($"   - Поповнено:  {response.LastRestockedAt.ToDateTime():dd.MM.yyyy HH:mm:ss} UTC");
            Console.ResetColor();
        }
        catch (RpcException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Помилка: {ex.StatusCode} - {ex.Status.Detail}");
            Console.ResetColor();
        }

        // 2. Успішне резервування залишків
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
            Console.WriteLine("✅ Резервування успішне:");
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

        // 3. Обробка помилки бізнес-правила (StatusCode.FailedPrecondition)
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
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ Перехоплено очікувану RpcException:");
            Console.WriteLine($"   - StatusCode: {ex.StatusCode}");
            Console.WriteLine($"   - Detail:     {ex.Status.Detail}");
            Console.ResetColor();
        }

        // 4. Обробка помилки відсутності товару (StatusCode.NotFound)
        Console.WriteLine("\n[4] Тест NotFound: Пошук неіснуючого товару 'NON-EXISTING-SKU-999'...");
        try
        {
            await client.GetStockItemAsync(new StockItemRequest { Sku = "NON-EXISTING-SKU-999" });
        }
        catch (RpcException ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ Перехоплено очікувану RpcException:");
            Console.WriteLine($"   - StatusCode: {ex.StatusCode}");
            Console.WriteLine($"   - Detail:     {ex.Status.Detail}");
            Console.ResetColor();
        }
    }
}
