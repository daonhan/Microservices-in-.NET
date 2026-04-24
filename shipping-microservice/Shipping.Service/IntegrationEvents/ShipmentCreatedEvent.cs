using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.IntegrationEvents;

public record ShipmentLineItem(int ProductId, int Quantity);

public record ShipmentCreatedEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    int WarehouseId,
    IReadOnlyList<ShipmentLineItem> Lines) : Event;
