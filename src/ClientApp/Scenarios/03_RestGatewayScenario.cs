using System.Net.Http.Json;
using System.Text.Json;

namespace ClientApp.Scenarios;

/// <summary>
/// Навчальний сценарій 03: Гібридна архітектура (REST API поверх gRPC мікросервісів).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// Цей сценарій демонструє роботу класичного зовнішнього веб-клієнта:
/// 1. Клієнт використовує стандартний .NET 'HttpClient' та надсилає текстовий HTTP/1.1 GET-запит
///    на порт 5000 шлюзу 'GatewayService' (/api/catalog/products/...).
/// 2. Шлюз 'GatewayService' приймає цей запит, перетворює його на двійкові виклики gRPC до InventoryService та PricingService,
///    отримує Protobuf-відповіді, компонує їх в анонімний об'єкт і серіалізує у JSON.
/// 3. Сценарій перевіряє також обробку помилок: як серверний gRPC статус 'StatusCode.NotFound'
///    транслюється шлюзом у стандартний HTTP 404 Not Found із JSON-повідомленням про помилку.
/// </summary>
public static class RestGatewayScenario
{
    /// <summary>
    /// Виконує тестування REST-ендпоінтів шлюзу за допомогою HttpClient.
    /// </summary>
    /// <param name="gatewayUrl">Базова адреса REST-ендпоінту GatewayService (http://localhost:5000).</param>
    public static async Task RunAsync(string gatewayUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=======================================================");
        Console.WriteLine(" 🌐 СЦЕНАРІЙ 03: ГІБРИДНА АРХІТЕКТУРА (REST-OVER-GRPC)");
        Console.WriteLine("=======================================================");
        Console.ResetColor();

        using var httpClient = new HttpClient { BaseAddress = new Uri(gatewayUrl) };

        // -----------------------------------------------------------------------------------------
        // ЧАСТИНА 1: Успішний REST GET запит через стандартний HTTP/1.1
        // -----------------------------------------------------------------------------------------
        string requestUrl = "/api/catalog/products/LAPTOP-PRO-16?tier=VIP&qty=2";
        Console.WriteLine($"\n[1] Відправка класичного HTTP REST запиту через HttpClient:");
        Console.WriteLine($"    GET {gatewayUrl}{requestUrl}");

        try
        {
            var response = await httpClient.GetAsync(requestUrl);
            var content = await response.Content.ReadAsStringAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ HTTP Статус: {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine("📄 Отримано JSON (агрегований шлюзом з бекенд gRPC-сервісів):");

            using var doc = JsonDocument.Parse(content);
            Console.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Помилка підключення до Gateway: {ex.Message}");
            Console.ResetColor();
        }

        // -----------------------------------------------------------------------------------------
        // ЧАСТИНА 2: Перевірка трансляції статусів (gRPC NotFound -> HTTP 404 Not Found)
        // -----------------------------------------------------------------------------------------
        string notFoundUrl = "/api/catalog/products/UNKNOWN-SKU-999";
        Console.WriteLine($"\n[2] Перевірка обробки помилок у REST фасаді:");
        Console.WriteLine($"    GET {gatewayUrl}{notFoundUrl}");

        try
        {
            var response = await httpClient.GetAsync(notFoundUrl);
            var content = await response.Content.ReadAsStringAsync();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ HTTP Статус: {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine($"   Тіло відповіді: {content}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Помилка: {ex.Message}");
            Console.ResetColor();
        }
    }
}
