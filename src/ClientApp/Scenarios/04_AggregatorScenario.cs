using Grpc.Net.Client;
using Mastery.Aggregator.V1;

namespace ClientApp.Scenarios;

public static class AggregatorScenario
{
    public static async Task RunAsync(string gatewayUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=======================================================");
        Console.WriteLine(" 🧩 СЦЕНАРІЙ 04: ПАТЕРН AGGREGATOR (SCATTER-GATHER)");
        Console.WriteLine("=======================================================");
        Console.ResetColor();

        using var channel = GrpcChannel.ForAddress(gatewayUrl);
        var client = new OrderAggregator.OrderAggregatorClient(channel);

        Console.WriteLine("\n[1] Відправка запиту до gRPC Агрегатора (OrderAggregator.GetOrderOverview)...");
        Console.WriteLine("    SKU: 'LAPTOP-PRO-16' | Грейд: 'Gold' | Кількість: 3 шт.");

        try
        {
            var response = await client.GetOrderOverviewAsync(new OrderOverviewRequest
            {
                Sku = "LAPTOP-PRO-16",
                CustomerTier = "Gold",
                Quantity = 3
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ Успішно отримано зведену Composite DTO (OrderOverviewResponse):");
            Console.WriteLine("------------------------------------------------------------------");
            Console.WriteLine($" 📦 Інвентар:");
            Console.WriteLine($"    - Назва товару:     {response.ProductName}");
            Console.WriteLine($"    - Артикул SKU:      {response.Sku}");
            Console.WriteLine($"    - Статус на складі: {response.StockStatus}");
            Console.WriteLine($"    - Доступний обсяг:  {response.AvailableStock} шт.");
            Console.WriteLine($"    - Основний склад:   {response.PrimaryWarehouse}");
            Console.WriteLine($" 💰 Ціноутворення:");
            Console.WriteLine($"    - Ціна за одиницю:  ${response.UnitPrice:F2} {response.Currency}");
            Console.WriteLine($"    - Загальна сума:    ${response.TotalPrice:F2} {response.Currency}");
            Console.WriteLine($"    - Надана знижка:    ${response.TotalDiscountApplied:F2}");
            Console.WriteLine($" ⚡ Метрики агрегації:");
            Console.WriteLine($"    - Затримка збору:   {response.AggregationLatencyMs} ms");
            Console.WriteLine($"    - Згенеровано:      {response.GeneratedAt.ToDateTime():dd.MM.yyyy HH:mm:ss} UTC");
            Console.WriteLine($"    - Оціночна ціна?:   {response.IsPriceEstimated}");
            if (!string.IsNullOrEmpty(response.DegradationWarning))
            {
                Console.WriteLine($"    - Попередження:     {response.DegradationWarning}");
            }
            Console.WriteLine("------------------------------------------------------------------");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Помилка виклику агрегатора: {ex.Message}");
            Console.ResetColor();
        }
    }
}
