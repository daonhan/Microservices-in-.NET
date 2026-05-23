namespace Inventory.Service.Features.ReserveByHttp;

public record ReservedLineDto(int ProductId, int WarehouseId, int Quantity);

public record ReserveResponse(Guid OrderId, IReadOnlyList<ReservedLineDto> Lines);
