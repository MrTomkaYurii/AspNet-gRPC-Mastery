namespace PricingService.Data;

public class ProductPriceEntity
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public double BasePrice { get; set; }
    public double StandardDiscountPercent { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
