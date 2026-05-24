namespace Payment.Service.Domain.Events;

public sealed record PaymentVoidedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string Reason) : IDomainEvent;
