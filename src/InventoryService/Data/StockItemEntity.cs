namespace InventoryService.Data;

/// <summary>
/// Сутність складської одиниці товару для Entity Framework Core.
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// Цей клас відображає таблицю 'StockItems' у базі даних SQLite 'inventory.db'.
/// Сутності БД навмисно відокремлені від контрактів Protobuf ('StockItemResponse'):
/// - Сутність БД оптимізована для реляційної схеми (первинні ключі Id, індекси, типи DateTime).
/// - Protobuf DTO оптимізований для двійкової серіалізації по мережі (теги полів 1, 2, 3, WellKnownTypes.Timestamp).
/// Мапінг між сутністю та контрактом виконується в сервісі 'InventoryGrpcService'.
/// </summary>
public class StockItemEntity
{
    /// <summary>Внутрішній первинний ключ запису СУБД (Auto-increment).</summary>
    public int Id { get; set; }

    /// <summary>Унікальний бізнес-артикул товару (Stock Keeping Unit), наприклад: 'LAPTOP-PRO-16'.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Повна комерційна назва продукту.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Поточна фізична кількість одиниць товару, доступних для продажу.</summary>
    public int AvailableQuantity { get; set; }

    /// <summary>Кількість одиниць, заблокованих під активні замовлення в процесі комплектації.</summary>
    public int ReservedQuantity { get; set; }

    /// <summary>Код складського терміналу розташування (наприклад: 'WH-KYIV-01').</summary>
    public string WarehouseCode { get; set; } = string.Empty;

    /// <summary>Статус наявності (1: InStock, 2: LowStock, 3: OutOfStock, 4: Discontinued).</summary>
    public int Status { get; set; }

    /// <summary>Дата та час останнього фізичного надходження партії на склад у форматі UTC.</summary>
    public DateTime LastRestockedAt { get; set; } = DateTime.UtcNow;
}
