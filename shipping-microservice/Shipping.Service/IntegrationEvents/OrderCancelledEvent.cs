using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.IntegrationEvents;

public record OrderCancelledEvent(Guid OrderId, string CustomerId) : Event;
