using GatewayService.Endpoints;
using GatewayService.Interceptors;
using GatewayService.Services;

// =================================================================================================
// Точка входу гібридного шлюзу та агрегатора (GatewayService)
// 
// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
// GatewayService — це центральний архітектурний компонент системи, який демонструє Dual-Stack Kestrel:
// 1. Порт 5000: Виділений для REST Minimal API (протокол HTTP/1.1).
//    Дозволяє звичайним браузерам, мобільним клієнтам та curl звертатися до каталогу у форматі JSON.
// 2. Порт 5005: Виділений для внутрішнього gRPC Агрегатора (чистий протокол HTTP/2 без шифрування h2c).
//    Служить точкою входу для високопродуктивних клієнтів, що використовують патерн Aggregator.
// 3. gRPC Client Factory ('AddGrpcClient'): налаштовує клієнти для взаємодії з InventoryService (5001)
//    та PricingService (5002) із вбудованим клієнтським перехоплювачем розподіленого трасування ('ClientTracingInterceptor').
// 4. Kestrel Interceptors: реєструє серверний перехоплювач логування ('ServerLoggingInterceptor').
// =================================================================================================

// Дозвіл на використання незашифрованого HTTP/2 (h2c) у сокетах .NET SocketsHttpHandler
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// КРОК 1: Реєстрація перехоплювачів (Interceptors) як Singleton сервісів
builder.Services.AddSingleton<ServerLoggingInterceptor>();
builder.Services.AddSingleton<ClientTracingInterceptor>();

// КРОК 2: Налаштування серверної gRPC-підсистеми із додаванням глобального перехоплювача логування
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;
    options.Interceptors.Add<ServerLoggingInterceptor>();
});

// КРОК 3: Реєстрація типізованих gRPC-клієнтів через GrpcClientFactory
// Фабрика автоматично керує пулом з'єднань 'SocketsHttpHandler' та життєвим циклом каналів.
// Метод 'AddInterceptor<ClientTracingInterceptor>()' автоматично додає трасування до кожного вихідного запиту.
builder.Services.AddGrpcClient<Mastery.Inventory.V1.InventoryService.InventoryServiceClient>(options =>
{
    var url = builder.Configuration["Services:InventoryUrl"] ?? "http://localhost:5001";
    options.Address = new Uri(url);
}).AddInterceptor<ClientTracingInterceptor>();

builder.Services.AddGrpcClient<Mastery.Pricing.V1.PricingService.PricingServiceClient>(options =>
{
    var url = builder.Configuration["Services:PricingUrl"] ?? "http://localhost:5002";
    options.Address = new Uri(url);
}).AddInterceptor<ClientTracingInterceptor>();

var app = builder.Build();

// КРОК 4: Монтування ендпоінтів REST Minimal API (Гібридний фасад для зовнішнього вебу)
app.MapCatalogEndpoints();

// КРОК 5: Монтування gRPC Агрегатора (Composite Service для gRPC-клієнтів)
app.MapGrpcService<OrderAggregatorService>();

// КРОК 6: Привітальний інформаційний екран для перегляду через браузер
app.MapGet("/", () => Results.Text(
    "🚪 [GatewayService] Гібридний Шлюз & gRPC Агрегатор запущено.\n" +
    " - REST Ендпоінт (HTTP/1.1): GET  http://localhost:5000/api/catalog/products/{sku}\n" +
    " - gRPC Ендпоінт (HTTP/2):   POST http://localhost:5005/mastery.aggregator.v1.OrderAggregator/GetOrderOverview\n",
    "text/plain; charset=utf-8"));

app.Run();
