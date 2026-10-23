using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using InventoryService.Data;
using Mastery.Inventory.V1;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Services;

/// <summary>
/// Серверна реалізація gRPC-сервісу управління складськими залишками (InventoryService).
/// 
/// НАВЧАЛЬНА ДОВІДКА ДЛЯ СТУДЕНТА:
/// 1. Клас успадковує 'Mastery.Inventory.V1.InventoryService.InventoryServiceBase' — абстрактний базовий клас,
///    автоматично згенерований компілятором 'protoc' на основі файлу 'inventory.proto'.
/// 2. Час життя екземпляра цього класу в ASP.NET Core DI — 'Scoped'. Це означає, що для кожного окремого
///    вхідного RPC-виклику Kestrel створює новий екземпляр сервісу та ізольований 'InventoryDbContext'.
/// 3. Усі методи є асинхронними ('async Task') і приймають параметр 'ServerCallContext context',
///    який надає доступ до HTTP/2 метаданих (Headers/Trailers), дедлайнів та токенів скасування.
/// </summary>
public class InventoryGrpcService : Mastery.Inventory.V1.InventoryService.InventoryServiceBase
{
    private readonly InventoryDbContext _dbContext;
    private readonly ILogger<InventoryGrpcService> _logger;

    /// <summary>
    /// Конструктор сервісу. Залежності впроваджуються через механізм Dependency Injection ASP.NET Core.
    /// </summary>
    /// <param name="dbContext">Ізольований контекст бази даних SQLite для поточного RPC-виклику.</param>
    /// <param name="logger">Типізований логер для діагностики та аудиту запитів.</param>
    public InventoryGrpcService(InventoryDbContext dbContext, ILogger<InventoryGrpcService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 1. UNARY RPC: Отримання інформації про товар за його унікальним артикулом (SKU).
    /// Модель зв'язку: Клієнт надсилає 1 повідомлення 'StockItemRequest' -> Сервер повертає 1 повідомлення 'StockItemResponse'.
    /// </summary>
    /// <param name="request">Вхідне повідомлення, що містить SKU товару для пошуку.</param>
    /// <param name="context">Контекст виклику: метадані, дедлайни, стан з'єднання та CancellationToken.</param>
    /// <returns>Знайдена сутність товару, трансформована у формат Protobuf DTO.</returns>
    /// <exception cref="RpcException">
    /// - StatusCode.InvalidArgument: якщо передано порожній або некоректний SKU.
    /// - StatusCode.NotFound: якщо товар з вказаним SKU відсутній у базі даних.
    /// </exception>
    public override async Task<StockItemResponse> GetStockItem(StockItemRequest request, ServerCallContext context)
    {
        _logger.LogInformation("--> [InventoryService] GetStockItem викликано для SKU: '{Sku}'", request.Sku);

        // КРОК 1: Вхідна валідація параметрів запиту
        // У gRPC помилки валідації повертаються не через HTTP 400, а через RpcException зі статусом InvalidArgument.
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            _logger.LogWarning("Спроба запиту залишку з порожнім SKU.");
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Артикул товару (SKU) не може бути порожнім."));
        }

        // КРОК 2: Асинхронний запит до бази даних через EF Core
        // ПЕДАГОГІЧНІ ОСОБЛИВОСТІ:
        // - '.AsNoTracking()': Обов'язково для операцій читання! Вимикає ChangeTracker в EF Core, що економить
        //   до 50% оперативної пам'яті та знімає навантаження зі збирача сміття (Garbage Collector).
        // - 'context.CancellationToken': Обов'язково передається у FirstOrDefaultAsync! Якщо клієнт розірвав
        //   з'єднання або сплив Deadline, драйвер SQLite миттєво перерве виконання SQL-запиту на дисковому рівні.
        var item = await _dbContext.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Sku == request.Sku, context.CancellationToken);

        // КРОК 3: Обробка ситуації відсутності запису в базі даних
        // Транслюємо 'null' у стандартний gRPC-статус NotFound (еквівалент HTTP 404 у REST).
        if (item is null)
        {
            _logger.LogWarning("Товар з SKU '{Sku}' не знайдено на складі.", request.Sku);
            throw new RpcException(new Status(StatusCode.NotFound, $"Товар з артикулом '{request.Sku}' не знайдено на складі."));
        }

        // КРОК 4: Пряма проєкція сутності EF Core у Protobuf-повідомлення (Zero Intermediate Allocations)
        return MapToResponse(item);
    }

    /// <summary>
    /// 2. UNARY RPC: Транзакційне резервування потрібної кількості товару на складі.
    /// Демонструє бізнес-валідацію залишків, атомарну зміну стану та збереження в СУБД.
    /// </summary>
    /// <param name="request">Запит на резервування: SKU, кількість та ідентифікатор замовлення.</param>
    /// <param name="context">Контекст RPC-виклику.</param>
    /// <returns>Підтвердження резервування з терміном дії блокування залишку.</returns>
    /// <exception cref="RpcException">
    /// - StatusCode.InvalidArgument: якщо кількість менша або дорівнює 0.
    /// - StatusCode.NotFound: якщо товар не існує.
    /// - StatusCode.FailedPrecondition: якщо на складі недостатньо вільних залишків для виконання операції.
    /// </exception>
    public override async Task<ReserveStockResponse> ReserveStock(ReserveStockRequest request, ServerCallContext context)
    {
        _logger.LogInformation("--> [InventoryService] ReserveStock: запит резерву {Quantity} шт. для SKU '{Sku}' (Order: '{OrderId}')",
            request.QuantityToReserve, request.Sku, request.OrderId);

        // КРОК 1: Валідація бізнес-обмежень аргументів
        if (request.QuantityToReserve <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Кількість для резервування має бути більшою за 0."));
        }

        // КРОК 2: Завантаження сутності з бази даних З ВІДСТЕЖЕННЯМ (Tracking)
        // Тут ми НЕ використовуємо AsNoTracking(), оскільки далі змінюватимемо властивості сутності та викличемо SaveChangesAsync().
        var item = await _dbContext.StockItems
            .FirstOrDefaultAsync(x => x.Sku == request.Sku, context.CancellationToken);

        if (item is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Товар з SKU '{request.Sku}' не знайдено."));
        }

        // КРОК 3: Бізнес-перевірка достатності складського залишку
        // Якщо клієнт просить більше, ніж є фізично — це порушення бізнес-передумови (FailedPrecondition).
        // У REST цьому відповідає HTTP 422 (Unprocessable Entity) або HTTP 400.
        if (item.AvailableQuantity < request.QuantityToReserve)
        {
            _logger.LogWarning("Неможливо зарезервувати {Requested} шт. для SKU '{Sku}'. Вільний залишок: {Available} шт.",
                request.QuantityToReserve, request.Sku, item.AvailableQuantity);

            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Недостатньо залишку на складі. Доступно: {item.AvailableQuantity}, запитано: {request.QuantityToReserve}."));
        }

        // КРОК 4: Модифікація стану сутності (зменшення доступного, збільшення зарезервованого)
        item.AvailableQuantity -= request.QuantityToReserve;
        item.ReservedQuantity += request.QuantityToReserve;

        // Автоматичне коригування статусу залежно від нового залишку
        if (item.AvailableQuantity == 0)
        {
            item.Status = 3; // OutOfStock
        }
        else if (item.AvailableQuantity < 10)
        {
            item.Status = 2; // LowStock
        }

        // КРОК 5: Атомарна фіксація транзакції у файлі SQLite (під захистом WAL-журналу)
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("--> [InventoryService] Успішно зарезервовано {Quantity} шт. для SKU '{Sku}'. Залишок: {Remaining}",
            request.QuantityToReserve, request.Sku, item.AvailableQuantity);

        // КРОК 6: Формування відповіді клієнту з наносекундною міткою часу Google Protobuf Timestamp
        return new ReserveStockResponse
        {
            IsSuccess = true,
            Sku = item.Sku,
            ReservedQuantity = request.QuantityToReserve,
            Message = $"Успішно зарезервовано {request.QuantityToReserve} шт. на складі {item.WarehouseCode}.",
            ReservationExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(2))
        };
    }

    /// <summary>
    /// 3. SERVER STREAMING RPC: Потокова трансляція складських залишків клієнту в реальному часі.
    /// Модель зв'язку: Клієнт надсилає 1 фільтр 'StockAuditFilter' -> Сервер відкриває HTTP/2 потік і шле N повідомлень.
    /// </summary>
    /// <param name="request">Критерії фільтрації: код складу та мінімальний поріг кількості.</param>
    /// <param name="responseStream">Канал запису у вихідний HTTP/2 потік (IServerStreamWriter).</param>
    /// <param name="context">Контекст RPC-виклику для моніторингу скасування підписки клієнтом.</param>
    public override async Task StreamStockUpdates(
        StockAuditFilter request,
        IServerStreamWriter<StockItemResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("--> [InventoryService] StreamStockUpdates: старт потоку аудиту (Склад: '{Warehouse}', Поріг: {Threshold})",
            request.WarehouseCode, request.MinQuantityThreshold);

        // Формуємо динамічний IQueryable запит
        var query = _dbContext.StockItems.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
        {
            query = query.Where(x => x.WarehouseCode == request.WarehouseCode);
        }

        if (request.MinQuantityThreshold > 0)
        {
            query = query.Where(x => x.AvailableQuantity >= request.MinQuantityThreshold);
        }

        var items = await query.ToListAsync(context.CancellationToken);

        // Ітеративна відправка повідомлень у відкритий HTTP/2 потік
        foreach (var item in items)
        {
            // ПЕРЕВІРКА СКАСУВАННЯ: Якщо клієнт розірвав з'єднання або спрацював таймаут (Deadline),
            // фрейм RST_STREAM переводить CancellationToken у стан Cancelled.
            // Перевірка дозволяє негайно перервати цикл і не виконувати марну роботу.
            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Клієнт достроково перервав стрімінг аудиту складу (RST_STREAM).");
                break;
            }

            var responseItem = MapToResponse(item);

            // ЗАПИС ФРЕЙМУ DATA: Метод WriteAsync асинхронно записує чергове Protobuf-повідомлення
            // у вихідний буфер Kestrel. Якщо клієнт не встигає читати (Backpressure), WriteAsync
            // стає на асинхронну паузу, доки клієнт не надішле фрейм WINDOW_UPDATE.
            await responseStream.WriteAsync(responseItem, context.CancellationToken);

            _logger.LogInformation("   [Stream Chunk Sent] Надіслано SKU: '{Sku}', Залишок: {Qty}", item.Sku, item.AvailableQuantity);

            // Штучна навчальна пауза (250 мс), щоб студент міг наочно спостерігати надходження чанків у консолі
            await Task.Delay(250, context.CancellationToken);
        }

        _logger.LogInformation("--> [InventoryService] StreamStockUpdates: потік успішно закрито. Передано позицій: {Count}", items.Count);
    }

    /// <summary>
    /// Допоміжний приватний метод прямої проєкції сутності EF Core у Protobuf DTO.
    /// Забезпечує Zero-Allocation підхід, уникаючи проміжних рядків та надлишкових об'єктів.
    /// </summary>
    private static StockItemResponse MapToResponse(StockItemEntity item)
    {
        return new StockItemResponse
        {
            Sku = item.Sku,
            ProductName = item.ProductName,
            AvailableQuantity = item.AvailableQuantity,
            ReservedQuantity = item.ReservedQuantity,
            WarehouseCode = item.WarehouseCode,
            // Безпечне явне приведення int з бази до строго типізованого перерахування Protobuf
            Status = (StockStatus)item.Status,
            // Конвертація DateTime в офіційний тип Google Protobuf Timestamp з точністю до наносекунд
            LastRestockedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.LastRestockedAt, DateTimeKind.Utc))
        };
    }
}
