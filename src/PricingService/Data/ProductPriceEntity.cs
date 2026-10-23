namespace PricingService.Data;

/// <summary>
/// Сутність цінової моделі продукту для Entity Framework Core.
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// Цей клас відображає таблицю 'Prices' у базі даних SQLite 'pricing.db'.
/// Зберігає базову одиничну вартість, поточний розмір стандартного дисконту та валюту розрахунків.
/// Використовується 'PricingGrpcService' для формування калькуляцій та торгів.
/// </summary>
public class ProductPriceEntity
{
    /// <summary>Внутрішній первинний ключ запису (Auto-increment ID).</summary>
    public int Id { get; set; }

    /// <summary>Унікальний артикул товару (SKU), що відповідає позиціям у складі InventoryService.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Базова номінальна ціна за 1 одиницю товару без знижок.</summary>
    public double BasePrice { get; set; }

    /// <summary>Поточний відсоток базової знижки за стандартною акційною програмою (0..100%).</summary>
    public double StandardDiscountPercent { get; set; }

    /// <summary>Міжнародний трилітерний код валюти за стандартом ISO 4217 (наприклад: 'USD').</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Мітка часу останнього оновлення ціни або знижки в UTC.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
