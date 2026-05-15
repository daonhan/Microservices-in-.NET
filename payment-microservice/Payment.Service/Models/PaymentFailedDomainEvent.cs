namespace Payment.Service.Models;

public sealed record PaymentFailedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string Reason) : IDomainEvent;
