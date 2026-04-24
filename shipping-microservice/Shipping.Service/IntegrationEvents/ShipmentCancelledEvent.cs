using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.IntegrationEvents;

public record ShipmentCancelledEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    DateTime OccurredAt,
    string? Reason) : Event;
