using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.IntegrationEvents.Events;

public record ShipmentDispatchedEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    string CarrierKey,
    string TrackingNumber,
    decimal QuotedPriceAmount,
    string QuotedPriceCurrency,
    DateTime OccurredAt) : Event;
