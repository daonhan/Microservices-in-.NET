using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record ShipmentLineItem(int ProductId, int Quantity);

public record ShipmentCreatedEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    int WarehouseId,
    IReadOnlyList<ShipmentLineItem> Lines) : Event;
