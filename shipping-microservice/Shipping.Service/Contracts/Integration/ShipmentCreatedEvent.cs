using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.Contracts.Integration;

public record ShipmentLineItem(int ProductId, int Quantity);

public record ShipmentCreatedEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    int WarehouseId,
    IReadOnlyList<ShipmentLineItem> Lines) : Event;
