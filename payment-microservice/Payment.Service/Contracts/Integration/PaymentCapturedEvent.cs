using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.Contracts.Integration;

public record PaymentCapturedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : Event;
