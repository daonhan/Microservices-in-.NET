using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.Contracts.Integration;

public record ShipmentCancelledEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    DateTime OccurredAt,
    string? Reason) : Event;
