using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.Contracts.Integration;

public record ShipmentReturnedEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    string? CarrierKey,
    string? TrackingNumber,
    string Reason,
    DateTime OccurredAt) : Event;
