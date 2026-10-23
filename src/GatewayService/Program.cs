using GatewayService.Endpoints;
using GatewayService.Interceptors;
using GatewayService.Services;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// 1. Реєстрація інтерцепторів
builder.Services.AddSingleton<ServerLoggingInterceptor>();
builder.Services.AddSingleton<ClientTracingInterceptor>();

// 2. Реєстрація gRPC сервера із серверним інтерцептором логування
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;
    options.Interceptors.Add<ServerLoggingInterceptor>();
});

// 3. Реєстрація типізованих gRPC клієнтів (GrpcClientFactory) із клієнтським інтерцептором трасування
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

// 4. Монтування REST Minimal API endpoints (Гібридний фасад для вебу)
app.MapCatalogEndpoints();

// 5. Монтування gRPC Агрегатора (Composite Service для gRPC-клієнтів)
app.MapGrpcService<OrderAggregatorService>();

// Привітальний екран при відкритті через браузер
app.MapGet("/", () => Results.Text(
    "🚪 [GatewayService] Гібридний Шлюз & gRPC Агрегатор запущено.\n" +
    " - REST Ендпоінт (HTTP/1.1): GET  http://localhost:5000/api/catalog/products/{sku}\n" +
    " - gRPC Ендпоінт (HTTP/2):   POST http://localhost:5005/mastery.aggregator.v1.OrderAggregator/GetOrderOverview\n",
    "text/plain; charset=utf-8"));

app.Run();
