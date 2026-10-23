using Grpc.Core;
using InventoryService.Data;
using InventoryService.Services;
using Mastery.Inventory.V1;
using Mastery.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mastery.Tests;

/// <summary>
/// Модульні тести для сервісу складу InventoryGrpcService.
/// Перевіряють транзакційне резервування, оптимізацію залишків та обробку граничних станів.
/// </summary>
public class InventoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly InventoryDbContext _dbContext;
    private readonly InventoryGrpcService _service;

    public InventoryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new InventoryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _dbContext.StockItems.Add(new StockItemEntity
        {
            Sku = "PHONE-ULTRA-24",
            ProductName = "Samsung Galaxy S24 Ultra",
            AvailableQuantity = 10,
            ReservedQuantity = 2,
            WarehouseCode = "WH-KYIV-01",
            Status = 1,
            LastRestockedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        _service = new InventoryGrpcService(_dbContext, NullLogger<InventoryGrpcService>.Instance);
    }

    [Fact]
    public async Task GetStockItem_WhenItemExists_ReturnsCorrectStockResponse()
    {
        // Arrange
        var request = new StockItemRequest { Sku = "PHONE-ULTRA-24" };
        var context = new TestServerCallContext();

        // Act
        var response = await _service.GetStockItem(request, context);

        // Assert
        Assert.Equal("PHONE-ULTRA-24", response.Sku);
        Assert.Equal("Samsung Galaxy S24 Ultra", response.ProductName);
        Assert.Equal(10, response.AvailableQuantity);
        Assert.Equal(2, response.ReservedQuantity);
        Assert.Equal("WH-KYIV-01", response.WarehouseCode);
        Assert.Equal(StockStatus.InStock, response.Status);
    }

    [Fact]
    public async Task GetStockItem_WhenItemDoesNotExist_ThrowsNotFound()
    {
        // Arrange
        var request = new StockItemRequest { Sku = "NON-EXISTENT" };
        var context = new TestServerCallContext();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.GetStockItem(request, context));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task ReserveStock_WhenEnoughStock_SuccessfullyUpdatesEntity()
    {
        // Arrange
        var request = new ReserveStockRequest
        {
            Sku = "PHONE-ULTRA-24",
            QuantityToReserve = 4,
            OrderId = "ORD-TEST-123"
        };
        var context = new TestServerCallContext();

        // Act
        var response = await _service.ReserveStock(request, context);

        // Assert
        Assert.True(response.IsSuccess);
        Assert.Equal(4, response.ReservedQuantity);

        // Перевіряємо збереження змін у базі даних
        var updated = await _dbContext.StockItems.FirstAsync(x => x.Sku == "PHONE-ULTRA-24");
        Assert.Equal(6, updated.AvailableQuantity);  // 10 - 4 = 6
        Assert.Equal(6, updated.ReservedQuantity);   // 2 + 4 = 6
    }

    [Fact]
    public async Task ReserveStock_WhenQuantityExceedsAvailable_ThrowsFailedPrecondition()
    {
        // Arrange
        var request = new ReserveStockRequest
        {
            Sku = "PHONE-ULTRA-24",
            QuantityToReserve = 999, // на складі всього 10
            OrderId = "ORD-OVERFLOW"
        };
        var context = new TestServerCallContext();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.ReserveStock(request, context));
        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
