namespace Order.Service.Models;

internal sealed record OrderCreatedDomainEvent(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<OrderItemSnapshot> Items,
    string Currency) : IDomainEvent;

internal sealed record OrderItemSnapshot(string ProductId, int Quantity, decimal UnitPrice);
