using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.IntegrationEvents;

public record OrderItem(string ProductId, int Quantity);

public record OrderCreatedEvent(Guid OrderId, string CustomerId, IReadOnlyList<OrderItem> Items) : Event;
