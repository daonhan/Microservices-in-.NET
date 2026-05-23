using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.Contracts.Integration;

public record PaymentAuthorizedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    string Currency) : Event;
