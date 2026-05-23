namespace Inventory.Service.Domain;

internal sealed record LowStockCrossing(
    int ProductId,
    int WarehouseId,
    int AvailableAfter,
    int ThresholdAfter);
