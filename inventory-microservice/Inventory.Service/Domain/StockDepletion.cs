namespace Inventory.Service.Domain;

internal sealed record StockDepletion(int ProductId, int WarehouseId);
