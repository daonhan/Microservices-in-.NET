namespace Shipping.Service.Features.GetShipmentsByOrder;

public record ShipmentLineDto(int ProductId, int Quantity);

public record ShipmentResponse(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    int WarehouseId,
    string Status,
    DateTime CreatedAt,
    IReadOnlyList<ShipmentLineDto> Lines);
