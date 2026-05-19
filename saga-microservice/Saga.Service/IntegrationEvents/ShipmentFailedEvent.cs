using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record ShipmentFailedEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    string? CarrierKey,
    string? TrackingNumber,
    string Reason,
    DateTime OccurredAt) : Event;
