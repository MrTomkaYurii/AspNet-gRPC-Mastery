namespace InventoryService.Data;

public class StockItemEntity
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public int Status { get; set; } // 1: InStock, 2: LowStock, 3: OutOfStock, 4: Discontinued
    public DateTime LastRestockedAt { get; set; } = DateTime.UtcNow;
}
