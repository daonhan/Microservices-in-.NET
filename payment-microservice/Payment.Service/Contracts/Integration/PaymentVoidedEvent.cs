using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.Contracts.Integration;

public record PaymentVoidedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string Reason) : Event;
