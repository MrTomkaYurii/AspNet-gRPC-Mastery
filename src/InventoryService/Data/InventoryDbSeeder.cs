using Microsoft.EntityFrameworkCore;

namespace InventoryService.Data;

public static class InventoryDbSeeder
{
    public static async Task SeedAsync(InventoryDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        if (await db.StockItems.AnyAsync())
        {
            return;
        }

        var items = new List<StockItemEntity>
        {
            new()
            {
                Sku = "LAPTOP-PRO-16",
                ProductName = "Apple MacBook Pro 16 (M3 Max / 36GB / 1TB)",
                AvailableQuantity = 45,
                ReservedQuantity = 5,
                WarehouseCode = "WH-KYIV-01",
                Status = 1, // InStock
                LastRestockedAt = DateTime.UtcNow.AddDays(-2)
            },
            new()
            {
                Sku = "PHONE-ULTRA-24",
                ProductName = "Samsung Galaxy S24 Ultra (512GB Titanium Gray)",
                AvailableQuantity = 120,
                ReservedQuantity = 10,
                WarehouseCode = "WH-LVIV-02",
                Status = 1, // InStock
                LastRestockedAt = DateTime.UtcNow.AddDays(-5)
            },
            new()
            {
                Sku = "HEADPHONES-ANC",
                ProductName = "Sony WH-1000XM5 Wireless Noise-Canceling Headphones",
                AvailableQuantity = 80,
                ReservedQuantity = 0,
                WarehouseCode = "WH-KYIV-01",
                Status = 1, // InStock
                LastRestockedAt = DateTime.UtcNow.AddDays(-1)
            },
            new()
            {
                Sku = "MONITOR-4K-32",
                ProductName = "Dell UltraSharp U3223QE 32\" 4K IPS Hub Monitor",
                AvailableQuantity = 8,
                ReservedQuantity = 3,
                WarehouseCode = "WH-ODESA-03",
                Status = 2, // LowStock
                LastRestockedAt = DateTime.UtcNow.AddDays(-14)
            },
            new()
            {
                Sku = "KEYBOARD-MECH",
                ProductName = "Keychron Q1 Pro Wireless Custom Mechanical Keyboard",
                AvailableQuantity = 0,
                ReservedQuantity = 0,
                WarehouseCode = "WH-KYIV-01",
                Status = 3, // OutOfStock
                LastRestockedAt = DateTime.UtcNow.AddDays(-30)
            }
        };

        await db.StockItems.AddRangeAsync(items);
        await db.SaveChangesAsync();
    }
}
