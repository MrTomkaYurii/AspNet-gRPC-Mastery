using Microsoft.EntityFrameworkCore;

namespace InventoryService.Data;

/// <summary>
/// Контекст бази даних SQLite для управління залишками складу (InventoryService).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// В архітектурі gRPC + EF Core кожен вхідний RPC-виклик виконується у власному DI-скоупі (Scoped).
/// Це гарантує:
/// 1. Відсутність стану між незалежними клієнтськими RPC-викликами.
/// 2. Потокобезпечність роботи з базою даних (DbContext не є Thread-Safe).
/// 3. Коректне звільнення дескрипторів з'єднань після завершення RPC-методу або його аварійного скасування.
/// </summary>
public class InventoryDbContext : DbContext
{
    /// <summary>
    /// Конструктор контексту, що приймає параметри конфігурації провайдера SQLite.
    /// </summary>
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    /// <summary>Набір сутностей складських позицій.</summary>
    public DbSet<StockItemEntity> StockItems => Set<StockItemEntity>();

    /// <summary>
    /// Конфігурація реляційної схеми за допомогою Fluent API EF Core.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StockItemEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Унікальний індекс за SKU для швидкого бінарного пошуку O(log N)
            entity.HasIndex(e => e.Sku).IsUnique();
            entity.Property(e => e.Sku).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.WarehouseCode).IsRequired().HasMaxLength(32);
        });
    }
}
