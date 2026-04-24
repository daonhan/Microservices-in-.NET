using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.IntegrationEvents;

public record ShipmentDeliveredEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    string? CarrierKey,
    string? TrackingNumber,
    DateTime OccurredAt) : Event;
