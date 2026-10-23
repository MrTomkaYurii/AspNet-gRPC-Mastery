using Grpc.Core;
using Mastery.Pricing.V1;
using Mastery.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PricingService.Data;
using PricingService.Services;

namespace Mastery.Tests;

/// <summary>
/// Модульні тести для gRPC-сервісу ціноутворення PricingGrpcService.
/// Демонструють роботу з in-memory SQLite базою даних та перевірку валідації статусів gRPC.
/// </summary>
public class PricingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PricingDbContext _dbContext;
    private readonly PricingGrpcService _service;

    public PricingServiceTests()
    {
        // Створюємо ізольоване in-memory SQLite з'єднання для кожного тесту
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PricingDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new PricingDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Додаємо тестовий продукт
        _dbContext.Prices.Add(new ProductPriceEntity
        {
            Sku = "LAPTOP-PRO-16",
            BasePrice = 2000.00,
            StandardDiscountPercent = 5.0,
            Currency = "USD",
            UpdatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        _service = new PricingGrpcService(_dbContext, NullLogger<PricingGrpcService>.Instance);
    }

    [Fact]
    public async Task GetProductPrice_ForVipClient_AppliesVipAndStandardDiscount()
    {
        // Arrange: 5% базова + 10% VIP = 15% знижка
        var request = new ProductPriceRequest
        {
            Sku = "LAPTOP-PRO-16",
            CustomerTier = "VIP",
            RequestedQuantity = 1
        };
        var context = new TestServerCallContext();

        // Act
        var response = await _service.GetProductPrice(request, context);

        // Assert
        Assert.Equal("LAPTOP-PRO-16", response.Sku);
        Assert.Equal(2000.00, response.BaseUnitPrice);
        Assert.Equal(15.0, response.DiscountPercentage);
        Assert.Equal(1700.00, response.FinalUnitPrice); // 2000 * 0.85 = 1700
    }

    [Fact]
    public async Task GetProductPrice_ForBulkQuantity_AppliesAdditionalBulkDiscount()
    {
        // Arrange: 5% базова + 3% bulk (за 10+ шт) = 8% знижка
        var request = new ProductPriceRequest
        {
            Sku = "LAPTOP-PRO-16",
            CustomerTier = "Standard",
            RequestedQuantity = 10
        };
        var context = new TestServerCallContext();

        // Act
        var response = await _service.GetProductPrice(request, context);

        // Assert
        Assert.Equal(8.0, response.DiscountPercentage);
        Assert.Equal(1840.00, response.FinalUnitPrice); // 2000 * 0.92 = 1840
    }

    [Fact]
    public async Task GetProductPrice_WithEmptySku_ThrowsInvalidArgument()
    {
        // Arrange
        var request = new ProductPriceRequest { Sku = "" };
        var context = new TestServerCallContext();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.GetProductPrice(request, context));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task GetProductPrice_WithUnknownSku_ThrowsNotFound()
    {
        // Arrange
        var request = new ProductPriceRequest { Sku = "NON-EXISTING-SKU" };
        var context = new TestServerCallContext();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.GetProductPrice(request, context));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
