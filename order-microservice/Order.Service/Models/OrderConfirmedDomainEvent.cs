namespace Order.Service.Models;

internal sealed record OrderConfirmedDomainEvent(Guid OrderId, string CustomerId) : IDomainEvent;
