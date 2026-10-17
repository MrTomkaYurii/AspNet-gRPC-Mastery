using Microsoft.EntityFrameworkCore;

namespace InventoryService.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<StockItemEntity> StockItems => Set<StockItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StockItemEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Sku).IsUnique();
            entity.Property(e => e.Sku).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.WarehouseCode).IsRequired().HasMaxLength(32);
        });
    }
}
