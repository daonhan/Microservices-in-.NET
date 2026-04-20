using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.IntegrationEvents;

public record OrderConfirmedEvent(Guid OrderId, string CustomerId) : Event;
