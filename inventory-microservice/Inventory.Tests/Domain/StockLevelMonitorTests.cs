using Inventory.Service.Models;

namespace Inventory.Tests.Domain;

public class StockLevelMonitorTests
{
    [Fact]
    public void TryLowStockCrossing_WhenThresholdRaisedAboveAvailable_ReturnsLowStockEvent()
    {
        var result = StockLevelMonitor.TryLowStockCrossing(
            productId: 1,
            warehouseId: 1,
            availableBefore: 5,
            availableAfter: 5,
            thresholdBefore: 0,
            thresholdAfter: 10);

        Assert.NotNull(result);
        Assert.Equal(1, result.ProductId);
        Assert.Equal(1, result.WarehouseId);
        Assert.Equal(5, result.Available);
        Assert.Equal(10, result.Threshold);
    }

    [Fact]
    public void TryLowStockCrossing_WhenAlreadyBelowThreshold_ReturnsNull()
    {
        var result = StockLevelMonitor.TryLowStockCrossing(
            productId: 1,
            warehouseId: 1,
            availableBefore: 5,
            availableAfter: 5,
            thresholdBefore: 10,
            thresholdAfter: 10);

        Assert.Null(result);
    }

    [Fact]
    public void TryLowStockCrossing_WhenAvailableEqualsThreshold_ReturnsLowStockEvent()
    {
        var result = StockLevelMonitor.TryLowStockCrossing(
            productId: 1,
            warehouseId: 1,
            availableBefore: 10,
            availableAfter: 10,
            thresholdBefore: 0,
            thresholdAfter: 10);

        Assert.NotNull(result);
        Assert.Equal(10, result.Available);
    }

    [Fact]
    public void TryLowStockCrossing_WhenRestockBackAboveThreshold_ReturnsNull()
    {
        var result = StockLevelMonitor.TryLowStockCrossing(
            productId: 1,
            warehouseId: 1,
            availableBefore: 3,
            availableAfter: 15,
            thresholdBefore: 10,
            thresholdAfter: 10);

        Assert.Null(result);
    }

    [Fact]
    public void TryLowStockCrossing_WhenThresholdZero_NeverFires()
    {
        var result = StockLevelMonitor.TryLowStockCrossing(
            productId: 1,
            warehouseId: 1,
            availableBefore: 5,
            availableAfter: 0,
            thresholdBefore: 0,
            thresholdAfter: 0);

        Assert.Null(result);
    }

    [Fact]
    public void TryDepletedCrossing_WhenAvailableReachesZero_ReturnsEvent()
    {
        var result = StockLevelMonitor.TryDepletedCrossing(
            productId: 1,
            warehouseId: 1,
            availableBefore: 3,
            availableAfter: 0);

        Assert.NotNull(result);
        Assert.Equal(1, result.ProductId);
        Assert.Equal(1, result.WarehouseId);
    }

    [Fact]
    public void TryDepletedCrossing_WhenAlreadyZero_ReturnsNull()
    {
        var result = StockLevelMonitor.TryDepletedCrossing(
            productId: 1,
            warehouseId: 1,
            availableBefore: 0,
            availableAfter: 0);

        Assert.Null(result);
    }

    [Fact]
    public void TryDepletedCrossing_WhenRestockFromZero_ReturnsNull()
    {
        var result = StockLevelMonitor.TryDepletedCrossing(
            productId: 1,
            warehouseId: 1,
            availableBefore: 0,
            availableAfter: 5);

        Assert.Null(result);
    }
}
