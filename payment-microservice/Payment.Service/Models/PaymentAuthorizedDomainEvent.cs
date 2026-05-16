namespace Payment.Service.Models;

public sealed record PaymentAuthorizedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    string Currency) : IDomainEvent;
