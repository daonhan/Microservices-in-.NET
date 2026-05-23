namespace Payment.Service.Domain.Events;

public sealed record PaymentRefundedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : IDomainEvent;
