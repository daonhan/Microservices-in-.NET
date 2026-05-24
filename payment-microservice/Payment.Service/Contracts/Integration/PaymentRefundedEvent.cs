using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.Contracts.Integration;

public record PaymentRefundedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : Event;
