using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.IntegrationEvents;

public record ShipmentDispatchedEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    string CarrierKey,
    string TrackingNumber,
    decimal QuotedPriceAmount,
    string QuotedPriceCurrency,
    DateTime OccurredAt) : Event;
