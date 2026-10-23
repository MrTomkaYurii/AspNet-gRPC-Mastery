using Microsoft.EntityFrameworkCore;

namespace PricingService.Data;

/// <summary>
/// Допоміжний клас для початкового наповнення бази даних цін та дисконтів.
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// Ініціалізує таблицю 'Prices' демонстраційними позиціями, що відповідають товарам зі складу InventoryService.
/// Дозволяє одразу після першого старту проєкту тестувати розрахунки вартості зі знижками,
/// клієнтські стріми оновлення цін та алгоритми інтерактивних торгів.
/// </summary>
public static class PricingDbSeeder
{
    /// <summary>
    /// Асинхронно створює таблиці бази даних SQLite 'pricing.db' та додає базові ціни.
    /// </summary>
    /// <param name="db">Контекст PricingDbContext.</param>
    public static async Task SeedAsync(PricingDbContext db)
    {
        // Створення файлу бази даних та структури таблиць
        await db.Database.EnsureCreatedAsync();

        // Захист від повторної ініціалізації
        if (await db.Prices.AnyAsync())
        {
            return;
        }

        // Каталог базових роздрібних цін у доларах США
        var prices = new List<ProductPriceEntity>
        {
            new()
            {
                Sku = "LAPTOP-PRO-16",
                BasePrice = 2499.00,
                StandardDiscountPercent = 5.0,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Sku = "PHONE-ULTRA-24",
                BasePrice = 1299.00,
                StandardDiscountPercent = 3.0,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Sku = "HEADPHONES-ANC",
                BasePrice = 399.00,
                StandardDiscountPercent = 10.0,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Sku = "MONITOR-4K-32",
                BasePrice = 899.00,
                StandardDiscountPercent = 7.5,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Sku = "KEYBOARD-MECH",
                BasePrice = 219.00,
                StandardDiscountPercent = 0.0,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow
            }
        };

        await db.Prices.AddRangeAsync(prices);
        await db.SaveChangesAsync();
    }
}
