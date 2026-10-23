using System.Text;
using ClientApp.Scenarios;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
Console.OutputEncoding = Encoding.UTF8;

string inventoryUrl = Environment.GetEnvironmentVariable("INVENTORY_URL") ?? "http://localhost:5001";
string pricingUrl = Environment.GetEnvironmentVariable("PRICING_URL") ?? "http://localhost:5002";
string gatewayRestUrl = Environment.GetEnvironmentVariable("GATEWAY_REST_URL") ?? "http://localhost:5000";
string gatewayGrpcUrl = Environment.GetEnvironmentVariable("GATEWAY_GRPC_URL") ?? "http://localhost:5005";

if (args.Length > 0 && args[0].Equals("--all", StringComparison.OrdinalIgnoreCase))
{
    await RunAllScenariosAsync();
    return;
}

while (true)
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine("======================================================================");
    Console.WriteLine(" 🚀 gRPC Complete Mastery (.NET 10) - Interactive Student Test Bench  ");
    Console.WriteLine("======================================================================");
    Console.ResetColor();
    Console.WriteLine($" 📍 Вузли: REST Gateway: {gatewayRestUrl} | gRPC Gateway: {gatewayGrpcUrl}");
    Console.WriteLine($"         Inventory: {inventoryUrl} | Pricing: {pricingUrl}");
    Console.WriteLine("----------------------------------------------------------------------");
    Console.WriteLine(" [1] Сценарій 01: Unary RPC & Обробка статус-кодів RpcException");
    Console.WriteLine(" [2] Сценарій 02: Потокові режими: Server, Client та Bi-directional");
    Console.WriteLine(" [3] Сценарій 03: Гібридний підхід: REST-over-gRPC через HttpClient");
    Console.WriteLine(" [4] Сценарій 04: Патерн Aggregator: Scatter-Gather та Composite DTO");
    Console.WriteLine(" [5] Сценарій 05: Deadlines, Каскадне скасування та Метадані");
    Console.WriteLine(" [A] Запустити ВСІ сценарії послідовно");
    Console.WriteLine(" [0] Вихід із програми");
    Console.WriteLine("======================================================================");
    Console.Write("Оберіть пункт меню (0-5, A): ");

    var choice = Console.ReadLine()?.Trim().ToUpperInvariant();

    if (choice == "0")
    {
        Console.WriteLine("\nЗавершення роботи. Дякуємо за використання gRPC Mastery!");
        break;
    }

    try
    {
        switch (choice)
        {
            case "1":
                await UnaryCallScenario.RunAsync(inventoryUrl);
                break;
            case "2":
                await StreamingScenario.RunAsync(inventoryUrl, pricingUrl);
                break;
            case "3":
                await RestGatewayScenario.RunAsync(gatewayRestUrl);
                break;
            case "4":
                await AggregatorScenario.RunAsync(gatewayGrpcUrl);
                break;
            case "5":
                await ResilienceScenario.RunAsync(gatewayGrpcUrl);
                break;
            case "A":
                await RunAllScenariosAsync();
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nНевідомий вибір. Спробуйте ще раз.");
                Console.ResetColor();
                break;
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n❌ Помилка виконання: {ex.Message}");
        Console.WriteLine("💡 Переконайтеся, що всі три мікросервіси (Gateway:5000, Inventory:5001, Pricing:5002) запущені.");
        Console.ResetColor();
    }

    Console.WriteLine("\nНатисніть будь-яку клавішу для повернення до головного меню...");
    Console.ReadKey(true);
}

async Task RunAllScenariosAsync()
{
    Console.WriteLine("\n>>> ЗАПУСК УСІХ 5-ти СЦЕНАРІЇВ ПІДРЯД <<<\n");
    await UnaryCallScenario.RunAsync(inventoryUrl);
    await Task.Delay(500);

    await StreamingScenario.RunAsync(inventoryUrl, pricingUrl);
    await Task.Delay(500);

    await RestGatewayScenario.RunAsync(gatewayRestUrl);
    await Task.Delay(500);

    await AggregatorScenario.RunAsync(gatewayGrpcUrl);
    await Task.Delay(500);

    await ResilienceScenario.RunAsync(gatewayGrpcUrl);
    Console.WriteLine("\n🎉 Усі навчальні сценарії успішно виконано!");
}
