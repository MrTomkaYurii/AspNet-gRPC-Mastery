using Grpc.Core;
using Grpc.Net.Client;
using Mastery.Aggregator.V1;

namespace ClientApp.Scenarios;

public static class ResilienceScenario
{
    public static async Task RunAsync(string gatewayUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=======================================================");
        Console.WriteLine(" 🛡️ СЦЕНАРІЙ 05: DEADLINES, CANCELLATION & RESILIENCE");
        Console.WriteLine("=======================================================");
        Console.ResetColor();

        using var channel = GrpcChannel.ForAddress(gatewayUrl);
        var client = new OrderAggregator.OrderAggregatorClient(channel);

        // 1. Тестування Deadlines (Крайнього терміну)
        Console.WriteLine("\n[1] Тест таймауту: Виклик агрегатора із жорстким дедлайном у 1 мс...");
        Console.WriteLine("    Очікуємо спрацювання Deadline Propagation та повернення StatusCode.DeadlineExceeded...");

        try
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(1);
            await client.GetOrderOverviewAsync(
                new OrderOverviewRequest { Sku = "LAPTOP-PRO-16", Quantity = 1 },
                deadline: deadline);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️ Перехоплено очікувану помилку StatusCode.DeadlineExceeded!");
            Console.WriteLine($"   - Деталь: {ex.Status.Detail}");
            Console.WriteLine("   💡 Клієнтська бібліотека надіслала HTTP/2 RST_STREAM фрейм.");
            Console.WriteLine("   💡 Серверний Kestrel негайно перервав виконання SQL-запитів та звільнив сокет.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Неочікувана помилка: {ex.Message}");
            Console.ResetColor();
        }

        // 2. Тестування метаданих та читання заголовків до отримання тіла
        Console.WriteLine("\n[2] Читання метаданих відповіді (Response Headers) до отримання основного тіла...");
        try
        {
            var call = client.GetOrderOverviewAsync(new OrderOverviewRequest
            {
                Sku = "PHONE-ULTRA-24",
                CustomerTier = "VIP",
                Quantity = 1
            });

            // Читаємо заголовки відповіді одразу, як тільки сервер їх відправив
            var headers = await call.ResponseHeadersAsync;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ Метадані заголовків (Headers), отримані від сервера:");
            foreach (var entry in headers)
            {
                Console.WriteLine($"   - {entry.Key}: {entry.Value}");
            }
            Console.ResetColor();

            // Отримуємо основне тіло
            var response = await call.ResponseAsync;
            Console.WriteLine($"   --> Успішно отримано товар: {response.ProductName} (Сума: ${response.TotalPrice:F2})");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Помилка: {ex.Message}");
            Console.ResetColor();
        }
    }
}
