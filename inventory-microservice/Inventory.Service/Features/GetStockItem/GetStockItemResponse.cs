namespace Inventory.Service.Features.GetStockItem;

public record GetStockItemResponse(
    int ProductId,
    int TotalOnHand,
    int TotalReserved,
    int Available,
    int Threshold,
    IReadOnlyList<StockLevelDto> PerWarehouse);

public record StockLevelDto(
    int WarehouseId,
    string WarehouseCode,
    int OnHand,
    int Reserved);
