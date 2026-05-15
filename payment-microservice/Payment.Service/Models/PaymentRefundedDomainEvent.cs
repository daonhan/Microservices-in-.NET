namespace Payment.Service.Models;

public sealed record PaymentRefundedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : IDomainEvent;
