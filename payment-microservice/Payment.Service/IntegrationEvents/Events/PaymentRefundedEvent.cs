using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.IntegrationEvents.Events;

public record PaymentRefundedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : Event;
