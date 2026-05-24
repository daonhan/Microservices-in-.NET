namespace Payment.Service.Domain.Events;

public sealed record PaymentAuthorizedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    string Currency) : IDomainEvent;
