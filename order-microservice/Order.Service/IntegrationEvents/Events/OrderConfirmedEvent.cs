using ECommerce.Shared.Infrastructure.EventBus;

namespace Order.Service.IntegrationEvents.Events;

public record OrderConfirmedEvent(Guid OrderId, string CustomerId) : Event;
