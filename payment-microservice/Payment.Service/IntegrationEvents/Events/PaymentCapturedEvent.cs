using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.IntegrationEvents.Events;

public record PaymentCapturedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : Event;
