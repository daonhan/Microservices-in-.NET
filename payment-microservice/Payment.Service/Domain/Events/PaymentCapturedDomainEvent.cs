namespace Payment.Service.Domain.Events;

public sealed record PaymentCapturedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : IDomainEvent;
