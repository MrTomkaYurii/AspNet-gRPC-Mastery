using Microsoft.EntityFrameworkCore;

namespace PricingService.Data;

public static class PricingDbSeeder
{
    public static async Task SeedAsync(PricingDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        if (await db.Prices.AnyAsync())
        {
            return;
        }

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
