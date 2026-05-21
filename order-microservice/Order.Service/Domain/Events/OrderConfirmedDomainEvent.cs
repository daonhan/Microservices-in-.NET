namespace Order.Service.Domain.Events;

internal sealed record OrderConfirmedDomainEvent(Guid OrderId, string CustomerId) : IDomainEvent;
