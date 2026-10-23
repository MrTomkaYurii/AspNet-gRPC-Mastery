using Microsoft.EntityFrameworkCore;

namespace PricingService.Data;

/// <summary>
/// Контекст бази даних SQLite для управління каталогом цін та дисконтів (PricingService).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// Реєструється у DI-контейнері ASP.NET Core зі скоупом Scoped.
/// Дозволяє ізольовано обробляти запити клієнтських стрімів (наприклад, пакетне оновлення цін 'UploadBulkDiscountRules')
/// без конфліктів стану з іншими паралельними з'єднаннями Kestrel.
/// </summary>
public class PricingDbContext : DbContext
{
    /// <summary>
    /// Конструктор контексту цін із передачею опцій підключення SQLite.
    /// </summary>
    public PricingDbContext(DbContextOptions<PricingDbContext> options) : base(options)
    {
    }

    /// <summary>Набір сутностей цінових моделей товарів.</summary>
    public DbSet<ProductPriceEntity> Prices => Set<ProductPriceEntity>();

    /// <summary>
    /// Налаштування обмежень та індексів таблиці цін.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProductPriceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Унікальний індекс за SKU для миттєвої фільтрації
            entity.HasIndex(e => e.Sku).IsUnique();
            entity.Property(e => e.Sku).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(8);
        });
    }
}
