using InventoryService.Data;
using InventoryService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Додавання EF Core з файловою SQLite базою
builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    options.UseSqlite("Data Source=inventory.db");
});

// Реєстрація gRPC інфраструктури
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;
});

var app = builder.Build();

// Автоматичне застосування міграцій та наповнення тестовими даними при старті
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await InventoryDbSeeder.SeedAsync(dbContext);
}

// Мапінг gRPC сервісу
app.MapGrpcService<InventoryGrpcService>();

// Підказка для звичайних HTTP/1.1 клієнтів (наприклад, перегляд через браузер)
app.MapGet("/", () => Results.Text(
    "⚡ [InventoryService] gRPC Сервіс Складу працює за протоколом HTTP/2 на порту 5001.\n" +
    "Підключення має відбуватися через gRPC клієнт або ClientApp.",
    "text/plain; charset=utf-8"));

app.Run();
