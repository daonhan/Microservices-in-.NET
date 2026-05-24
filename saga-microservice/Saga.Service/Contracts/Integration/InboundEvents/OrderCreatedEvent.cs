using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record OrderItem(string ProductId, int Quantity, decimal UnitPrice = 0m);

public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<OrderItem> Items,
    string Currency = "USD") : Event;
