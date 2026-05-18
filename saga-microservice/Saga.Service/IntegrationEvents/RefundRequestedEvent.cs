using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record RefundRequestedEvent(
    Guid OrderId,
    Guid PaymentId,
    Guid? ShipmentId,
    decimal RefundAmount,
    string Currency = "USD") : Event;
