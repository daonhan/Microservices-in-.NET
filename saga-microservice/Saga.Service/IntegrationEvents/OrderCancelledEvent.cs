using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record OrderCancelledEvent(Guid OrderId, string CustomerId) : Event;
