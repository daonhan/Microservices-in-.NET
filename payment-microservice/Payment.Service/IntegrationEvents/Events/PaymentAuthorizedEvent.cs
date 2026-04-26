using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.IntegrationEvents.Events;

public record PaymentAuthorizedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    string Currency) : Event;
