using Microsoft.EntityFrameworkCore;
using PricingService.Data;
using PricingService.Services;

var builder = WebApplication.CreateBuilder(args);

// Додавання EF Core з файловою SQLite базою
builder.Services.AddDbContext<PricingDbContext>(options =>
{
    options.UseSqlite("Data Source=pricing.db");
});

// Реєстрація gRPC інфраструктури
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;
});

var app = builder.Build();

// Автоматичне застосування схеми та наповнення тестовими цінами
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
    await PricingDbSeeder.SeedAsync(dbContext);
}

// Мапінг gRPC сервісу
app.MapGrpcService<PricingGrpcService>();

// Fallback підказка для веб-браузера
app.MapGet("/", () => Results.Text(
    "⚡ [PricingService] gRPC Сервіс Ціноутворення працює за протоколом HTTP/2 на порту 5002.\n" +
    "Підключення має відбуватися через gRPC клієнт або GatewayService.",
    "text/plain; charset=utf-8"));

app.Run();
