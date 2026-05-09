namespace Payment.Service.Models;

public sealed record PaymentCapturedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : IDomainEvent;
