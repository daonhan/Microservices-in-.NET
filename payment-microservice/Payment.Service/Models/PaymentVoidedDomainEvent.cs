namespace Payment.Service.Models;

public sealed record PaymentVoidedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string Reason) : IDomainEvent;
