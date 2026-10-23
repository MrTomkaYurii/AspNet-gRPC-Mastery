using System.Net.Http.Json;
using System.Text.Json;

namespace ClientApp.Scenarios;

public static class RestGatewayScenario
{
    public static async Task RunAsync(string gatewayUrl)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=======================================================");
        Console.WriteLine(" 🌐 СЦЕНАРІЙ 03: ГІБРИДНА АРХІТЕКТУРА (REST-OVER-GRPC)");
        Console.WriteLine("=======================================================");
        Console.ResetColor();

        using var httpClient = new HttpClient { BaseAddress = new Uri(gatewayUrl) };

        // 1. Успішний REST GET запит через звичайний HTTP/1.1
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

        // 2. Тест 404 Not Found трансляції з gRPC RpcException(NotFound)
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
