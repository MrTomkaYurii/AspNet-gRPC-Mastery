using Grpc.Core;
using Grpc.Net.Client;
using Mastery.Aggregator.V1;

namespace ClientApp.Scenarios;

/// <summary>
/// Навчальний сценарій 05: Стійкість gRPC — Deadlines, каскадне скасування (Cancellation) та робота з метаданими (Metadata).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// 1. Deadlines (Крайні терміни) — фундаментальний механізм стабільності gRPC.
///    Клієнт передає абсолютний час (UTC), до якого він очікує відповідь.
///    Якщо сервер не встигає відповісти до цього моменту, клієнтська бібліотека негайно надсилає
///    HTTP/2 фрейм RST_STREAM з кодом CANCEL, звільняючи ресурси клієнта.
/// 2. Deadline Propagation: сервер Kestrel автоматично транслює отриманий дедлайн у 'context.CancellationToken'.
///    Це гарантує, що всі внутрішні SQL-запити в SQLite та наступні downstream gRPC-виклики скасовуються
///    каскадно, не витрачаючи процесорний час на запити, які вже нікому не потрібні.
/// 3. Metadata (Заголовки): gRPC дозволяє отримувати HTTP/2 HEADERS до отримання основного тіла повідомлення
///    за допомогою властивості 'call.ResponseHeadersAsync'.
/// </summary>
public static class ResilienceScenario
{
    /// <summary>
    /// Демонструє спрацювання таймауту дедлайну та читання метаданих відповіді.
    /// </summary>
    /// <param name="gatewayGrpcUrl">Адреса gRPC-інтерфейсу GatewayService (http://localhost:5005).</param>
    public static async Task RunAsync(string gatewayGrpcUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=======================================================");
        Console.WriteLine(" 🛡️ СЦЕНАРІЙ 05: DEADLINES, CANCELLATION & RESILIENCE");
        Console.WriteLine("=======================================================");
        Console.ResetColor();

        using var channel = GrpcChannel.ForAddress(gatewayGrpcUrl);
        var client = new OrderAggregator.OrderAggregatorClient(channel);

        // -----------------------------------------------------------------------------------------
        // КРОК 1: Тестування Deadlines (Крайнього терміну)
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[1] Тест таймауту: Виклик агрегатора із нереалістично жорстким дедлайном у 1 мс...");
        Console.WriteLine("    Очікуємо спрацювання Deadline Propagation та повернення StatusCode.DeadlineExceeded...");

        try
        {
            // Встановлюємо дедлайн: поточний час + 1 мілісекунда
            var deadline = DateTime.UtcNow.AddMilliseconds(1);
            await client.GetOrderOverviewAsync(
                new OrderOverviewRequest { Sku = "LAPTOP-PRO-16", Quantity = 1 },
                deadline: deadline);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️ Перехоплено очікувану помилку StatusCode.DeadlineExceeded!");
            Console.WriteLine($"   - Деталь помилки: {ex.Status.Detail}");
            Console.WriteLine("   💡 Клієнтська бібліотека згенерувала та надіслала HTTP/2 RST_STREAM фрейм.");
            Console.WriteLine("   💡 Сервер Kestrel отримав скасування через CancellationToken та перервав I/O операції.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Неочікувана помилка: {ex.Message}");
            Console.ResetColor();
        }

        // -----------------------------------------------------------------------------------------
        // КРОК 2: Робота з HTTP/2 метаданими (Response Headers) до вичитування тіла
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("\n[2] Читання метаданих відповіді (Response Headers) до отримання основного тіла...");
        try
        {
            // Створюємо дескриптор асинхронного Unary-виклику
            var call = client.GetOrderOverviewAsync(new OrderOverviewRequest
            {
                Sku = "PHONE-ULTRA-24",
                CustomerTier = "VIP",
                Quantity = 1
            });

            // Асинхронно чекаємо виключно на фрейм HTTP/2 HEADERS від сервера
            var headers = await call.ResponseHeadersAsync;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ Метадані заголовків (Headers), успішно отримані від сервера:");
            foreach (var entry in headers)
            {
                Console.WriteLine($"   - Заголовок: '{entry.Key}' = '{entry.Value}'");
            }
            Console.ResetColor();

            // Після цього чекаємо на фрейм DATA з основним корисним навантаженням Protobuf
            var response = await call.ResponseAsync;
            Console.WriteLine($"   --> Успішно отримано об'єкт: {response.ProductName} (Сума: ${response.TotalPrice:F2})");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Помилка: {ex.Message}");
            Console.ResetColor();
        }
    }
}
