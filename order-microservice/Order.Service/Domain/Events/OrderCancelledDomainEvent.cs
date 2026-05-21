namespace Order.Service.Domain.Events;

internal sealed record OrderCancelledDomainEvent(Guid OrderId, string CustomerId) : IDomainEvent;
