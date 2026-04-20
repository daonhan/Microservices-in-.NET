using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.IntegrationEvents;

public record OrderCancelledEvent(Guid OrderId, string CustomerId) : Event;
