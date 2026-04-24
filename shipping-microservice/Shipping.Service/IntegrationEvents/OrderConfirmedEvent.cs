using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.IntegrationEvents;

public record OrderConfirmedEvent(Guid OrderId, string CustomerId) : Event;
