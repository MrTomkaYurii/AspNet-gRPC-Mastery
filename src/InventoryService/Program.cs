using InventoryService.Data;
using InventoryService.Services;
using Microsoft.EntityFrameworkCore;

// =================================================================================================
// Точка входу мікросервісу управління складськими залишками (InventoryService)
// 
// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
// Цей проєкт побудовано на ASP.NET Core 10 з використанням Kestrel веб-сервера.
// Особливості конфігурації:
// 1. Kestrel налаштовано на прослуховування порту 5001 з протоколом HTTP/2 (h2c — HTTP/2 cleartext).
// 2. База даних: Entity Framework Core 10 з провайдером SQLite ('inventory.db').
// 3. gRPC-ендпоінти монтуються через 'app.MapGrpcService<InventoryGrpcService>()'.
// 4. gRPC Server Reflection: сервіс самоопису API для Postman, BloomRPC та grpcurl.
// =================================================================================================

var builder = WebApplication.CreateBuilder(args);

// КРОК 1: Конфігурація Entity Framework Core з файловою СУБД SQLite
// Сервіс реєструється зі скоупом Scoped, тому для кожного окремого gRPC-запиту
// створюється свій власний екземпляр DbContext та підключення до бази.
builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    options.UseSqlite("Data Source=inventory.db");
});

// КРОК 2: Реєстрація gRPC інфраструктури в ASP.NET Core
// 'EnableDetailedErrors = true' вмикає детальне логування та передачу stack trace винятків
// для розробки та налагодження (у production рекомендується вимикати з міркувань безпеки).
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;
});

// Додавання стандартного протоколу gRPC Server Reflection (grpc.reflection.v1alpha / v1)
builder.Services.AddGrpcReflection();

var app = builder.Build();

// КРОК 3: Ініціалізація та наповнення БД початковими демонстраційними даними (Seeding)
// Створюємо IServiceScope, отримуємо екземпляр InventoryDbContext і викликаємо міграцію
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await InventoryDbSeeder.SeedAsync(dbContext);
}

// КРОК 4: Реєстрація gRPC-сервісу у конвеєрі маршрутизації Kestrel
// Kestrel зіставляє вхідні HTTP/2 POST запити з шляхами вигляду:
// /{Package}.{Service}/{Method} (наприклад: /mastery.inventory.v1.InventoryService/GetStockItem)
app.MapGrpcService<InventoryGrpcService>();

// Реєстрація ендпоінта gRPC Reflection для динамічного виявлення сервісів зовнішніми інструментами
app.MapGrpcReflectionService();

// КРОК 5: Fallback-ендпоінт для зручності: інформує користувача у разі відкриття через звичайний браузер
app.MapGet("/", () => Results.Text(
    "⚡ [InventoryService] gRPC Сервіс Складу працює за протоколом HTTP/2 на порту 5001.\n" +
    " - gRPC Reflection: Активно (підтримується Postman, grpcurl, BloomRPC)\n" +
    "Підключення має відбуватися через gRPC клієнт або ClientApp.",
    "text/plain; charset=utf-8"));

app.Run();
