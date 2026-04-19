namespace Inventory.Service.ApiModels;

public record StockMovementDto(
    long Id,
    int ProductId,
    int WarehouseId,
    string Type,
    int Quantity,
    DateTime OccurredAt,
    Guid? OrderId,
    string? Reason);
