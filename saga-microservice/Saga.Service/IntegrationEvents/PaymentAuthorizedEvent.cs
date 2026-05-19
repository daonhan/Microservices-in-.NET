using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record PaymentAuthorizedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    string Currency) : Event;
