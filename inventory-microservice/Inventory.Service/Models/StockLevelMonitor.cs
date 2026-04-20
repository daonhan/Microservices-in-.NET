using Inventory.Service.IntegrationEvents;

namespace Inventory.Service.Models;

internal static class StockLevelMonitor
{
    public static LowStockEvent? TryLowStockCrossing(
        int productId,
        int warehouseId,
        int availableBefore,
        int availableAfter,
        int thresholdBefore,
        int thresholdAfter)
    {
        var wasLowStock = thresholdBefore > 0 && availableBefore <= thresholdBefore;
        var isLowStock = thresholdAfter > 0 && availableAfter <= thresholdAfter;

        return !wasLowStock && isLowStock
            ? new LowStockEvent(productId, warehouseId, availableAfter, thresholdAfter)
            : null;
    }

    public static StockDepletedEvent? TryDepletedCrossing(
        int productId,
        int warehouseId,
        int availableBefore,
        int availableAfter)
    {
        return availableBefore > 0 && availableAfter == 0
            ? new StockDepletedEvent(productId, warehouseId)
            : null;
    }
}
