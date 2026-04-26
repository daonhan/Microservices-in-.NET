using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.IntegrationEvents.Events;

public record OrderCancelledEvent(Guid OrderId, string CustomerId) : Event;
