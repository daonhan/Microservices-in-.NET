using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.IntegrationEvents.Events;

public record OrderItem(string ProductId, int Quantity, decimal UnitPrice = 0m);

public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<OrderItem> Items,
    string Currency = "USD") : Event;
