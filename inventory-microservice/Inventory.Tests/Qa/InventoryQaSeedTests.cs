using ECommerce.Shared.Observability.Metrics;
using ECommerce.Shared.Qa;
using Inventory.Service.Infrastructure.Data.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Tests.Qa;

public class InventoryQaSeedTests
{
    [Fact]
    public async Task GivenInventoryModelCreated_WhenReadingProductHappyStock_ThenSufficientStockExists()
    {
        var options = new DbContextOptionsBuilder<InventoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new InventoryContext(options, new MetricFactory("Inventory.Tests"));
        await context.Database.EnsureCreatedAsync();

        var stockItem = await context.StockItems.SingleAsync(s => s.ProductId == QaPersonas.ProductHappyId);
        var stockLevel = await context.StockLevels.SingleAsync(s =>
            s.ProductId == QaPersonas.ProductHappyId && s.WarehouseId == QaPersonas.DefaultWarehouseId);

        Assert.Equal(QaPersonas.HappyPathStockOnHand, stockItem.TotalOnHand);
        Assert.Equal(0, stockItem.TotalReserved);
        Assert.Equal(QaPersonas.HappyPathStockOnHand, stockLevel.OnHand);
        Assert.Equal(0, stockLevel.Reserved);
    }

    [Fact]
    public async Task GivenInventoryModelCreated_WhenReadingProductDeclineStock_ThenSufficientStockExists()
    {
        var options = new DbContextOptionsBuilder<InventoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new InventoryContext(options, new MetricFactory("Inventory.Tests"));
        await context.Database.EnsureCreatedAsync();

        var stockItem = await context.StockItems.SingleAsync(s => s.ProductId == QaPersonas.ProductDeclineId);
        var stockLevel = await context.StockLevels.SingleAsync(s =>
            s.ProductId == QaPersonas.ProductDeclineId && s.WarehouseId == QaPersonas.DefaultWarehouseId);

        Assert.Equal(QaPersonas.DeclinePathStockOnHand, stockItem.TotalOnHand);
        Assert.Equal(0, stockItem.TotalReserved);
        Assert.Equal(QaPersonas.DeclinePathStockOnHand, stockLevel.OnHand);
        Assert.Equal(0, stockLevel.Reserved);
    }

    [Fact]
    public async Task GivenInventoryModelCreated_WhenReadingProductZeroStock_ThenStockOnHandIsZero()
    {
        var options = new DbContextOptionsBuilder<InventoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new InventoryContext(options, new MetricFactory("Inventory.Tests"));
        await context.Database.EnsureCreatedAsync();

        var stockItem = await context.StockItems.SingleAsync(s => s.ProductId == QaPersonas.ProductZeroStockId);
        var stockLevel = await context.StockLevels.SingleAsync(s =>
            s.ProductId == QaPersonas.ProductZeroStockId && s.WarehouseId == QaPersonas.DefaultWarehouseId);

        Assert.Equal(0, stockItem.TotalOnHand);
        Assert.Equal(0, stockItem.TotalReserved);
        Assert.Equal(0, stockLevel.OnHand);
        Assert.Equal(0, stockLevel.Reserved);
    }
}
