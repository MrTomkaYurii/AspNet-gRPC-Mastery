using Microsoft.EntityFrameworkCore;
using PricingService.Data;
using PricingService.Services;

// =================================================================================================
// Точка входу мікросервісу ціноутворення та котирувань (PricingService)
// 
// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
// Цей мікросервіс працює на порту 5002 виключно за протоколом HTTP/2.
// Він обслуговує запити розрахунку індивідуальних дисконтів, потік масового завантаження правил
// (Client Streaming) та повнодуплексні інтерактивні торги (Bi-directional Streaming).
// База даних SQLite 'pricing.db' керується через EF Core 10.
// Підтримує gRPC Server Reflection для автоматичного розпізнавання методів у Postman.
// =================================================================================================

var builder = WebApplication.CreateBuilder(args);

// КРОК 1: Додавання контексту бази даних SQLite
builder.Services.AddDbContext<PricingDbContext>(options =>
{
    options.UseSqlite("Data Source=pricing.db");
});

// КРОК 2: Реєстрація gRPC інфраструктури
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;
});

// Додавання gRPC Server Reflection
builder.Services.AddGrpcReflection();

var app = builder.Build();

// КРОК 3: Автоматичне застосування схеми таблиць та наповнення прайс-листа стартовими даними
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
    await PricingDbSeeder.SeedAsync(dbContext);
}

// КРОК 4: Реєстрація gRPC-сервісу ціноутворення у маршрутизаторі Kestrel
app.MapGrpcService<PricingGrpcService>();

// Реєстрація ендпоінта gRPC Reflection
app.MapGrpcReflectionService();

// КРОК 5: Fallback-інформація для звичайних HTTP/1.1 клієнтів (браузер)
app.MapGet("/", () => Results.Text(
    "⚡ [PricingService] gRPC Сервіс Ціноутворення працює за протоколом HTTP/2 на порту 5002.\n" +
    " - gRPC Reflection: Активно (підтримується Postman, grpcurl, BloomRPC)\n" +
    "Підключення має відбуватися через gRPC клієнт або GatewayService.",
    "text/plain; charset=utf-8"));

app.Run();
