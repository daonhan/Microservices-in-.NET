namespace Inventory.Service.ApiModels;

public record ReservedLineDto(int ProductId, int WarehouseId, int Quantity);

public record ReserveResponse(Guid OrderId, IReadOnlyList<ReservedLineDto> Lines);
