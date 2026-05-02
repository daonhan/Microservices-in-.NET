namespace Order.Service.Models;

internal sealed record OrderCancelledDomainEvent(Guid OrderId, string CustomerId) : IDomainEvent;
