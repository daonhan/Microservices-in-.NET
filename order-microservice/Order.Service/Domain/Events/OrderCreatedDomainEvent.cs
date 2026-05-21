namespace Order.Service.Domain.Events;

internal sealed record OrderCreatedDomainEvent(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<OrderItemSnapshot> Items,
    string Currency) : IDomainEvent;

internal sealed record OrderItemSnapshot(string ProductId, int Quantity, decimal UnitPrice);
