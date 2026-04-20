using ECommerce.Shared.Infrastructure.EventBus;

namespace Order.Service.IntegrationEvents.Events;

public record OrderCancelledEvent(Guid OrderId, string CustomerId) : Event;
