using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record OrderItem(string ProductId, int Quantity, decimal UnitPrice = 0m);

public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<OrderItem> Items,
    string Currency = "USD") : Event;
