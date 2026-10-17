using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using InventoryService.Data;
using Mastery.Inventory.V1;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Services;

/// <summary>
/// Реалізація gRPC-сервісу складу.
/// Демонструє Unary-запити, Server Streaming, валідацію залишків та статус-коди RpcException.
/// </summary>
public class InventoryGrpcService : Mastery.Inventory.V1.InventoryService.InventoryServiceBase
{
    private readonly InventoryDbContext _dbContext;
    private readonly ILogger<InventoryGrpcService> _logger;

    public InventoryGrpcService(InventoryDbContext dbContext, ILogger<InventoryGrpcService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 1. Unary RPC: Отримання позиції товару за артикулом.
    /// </summary>
    public override async Task<StockItemResponse> GetStockItem(StockItemRequest request, ServerCallContext context)
    {
        _logger.LogInformation("--> [InventoryService] GetStockItem викликано для SKU: '{Sku}'", request.Sku);

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Артикул товару (SKU) не може бути порожнім."));
        }

        var item = await _dbContext.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Sku == request.Sku, context.CancellationToken);

        if (item is null)
        {
            _logger.LogWarning("Товар з SKU '{Sku}' не знайдено в базі даних.", request.Sku);
            throw new RpcException(new Status(StatusCode.NotFound, $"Товар з артикулом '{request.Sku}' не знайдено на складі."));
        }

        return MapToResponse(item);
    }

    /// <summary>
    /// 2. Unary RPC: Резервування залишку з транзакційною валідацією.
    /// </summary>
    public override async Task<ReserveStockResponse> ReserveStock(ReserveStockRequest request, ServerCallContext context)
    {
        _logger.LogInformation("--> [InventoryService] ReserveStock: спроба резерву {Quantity} шт. для SKU '{Sku}' (Order: '{OrderId}')",
            request.QuantityToReserve, request.Sku, request.OrderId);

        if (request.QuantityToReserve <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Кількість для резервування має бути більшою за 0."));
        }

        var item = await _dbContext.StockItems
            .FirstOrDefaultAsync(x => x.Sku == request.Sku, context.CancellationToken);

        if (item is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Товар з SKU '{request.Sku}' не знайдено."));
        }

        if (item.AvailableQuantity < request.QuantityToReserve)
        {
            _logger.LogWarning("Неможливо зарезервувати {Requested} шт. для SKU '{Sku}'. Доступно лише {Available} шт.",
                request.QuantityToReserve, request.Sku, item.AvailableQuantity);

            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Недостатньо залишку на складі. Доступно: {item.AvailableQuantity}, запитано: {request.QuantityToReserve}."));
        }

        // Резервування
        item.AvailableQuantity -= request.QuantityToReserve;
        item.ReservedQuantity += request.QuantityToReserve;
        if (item.AvailableQuantity == 0)
        {
            item.Status = 3; // OutOfStock
        }
        else if (item.AvailableQuantity < 10)
        {
            item.Status = 2; // LowStock
        }

        await _dbContext.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("--> [InventoryService] Успішно зарезервовано {Quantity} шт. для SKU '{Sku}'. Залишок: {Remaining}",
            request.QuantityToReserve, request.Sku, item.AvailableQuantity);

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
    /// 3. Server Streaming RPC: Потокова трансляція наявних складських позицій клієнту.
    /// Клієнт відкриває один запит, а сервер шле пачки даних через IServerStreamWriter.
    /// </summary>
    public override async Task StreamStockUpdates(
        StockAuditFilter request,
        IServerStreamWriter<StockItemResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("--> [InventoryService] StreamStockUpdates: початок потоку аудиту (Склад: '{Warehouse}', Поріг: {Threshold})",
            request.WarehouseCode, request.MinQuantityThreshold);

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

        foreach (var item in items)
        {
            // Перевіряємо, чи клієнт не скасував запит (каскадний дедлайн / Cancellation)
            if (context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Клієнт перервав стрімінг аудиту складу.");
                break;
            }

            var responseItem = MapToResponse(item);
            await responseStream.WriteAsync(responseItem, context.CancellationToken);

            _logger.LogInformation("   [Stream Chunk Sent] Надіслано SKU: '{Sku}', Залишок: {Qty}", item.Sku, item.AvailableQuantity);

            // Невелика штучна пауза для наочності спостереження потоку на стороні клієнта
            await Task.Delay(250, context.CancellationToken);
        }

        _logger.LogInformation("--> [InventoryService] StreamStockUpdates: потік успішно завершено. Передано позицій: {Count}", items.Count);
    }

    private static StockItemResponse MapToResponse(StockItemEntity item)
    {
        return new StockItemResponse
        {
            Sku = item.Sku,
            ProductName = item.ProductName,
            AvailableQuantity = item.AvailableQuantity,
            ReservedQuantity = item.ReservedQuantity,
            WarehouseCode = item.WarehouseCode,
            Status = (StockStatus)item.Status,
            LastRestockedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.LastRestockedAt, DateTimeKind.Utc))
        };
    }
}
