using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record OrderConfirmedEvent(Guid OrderId, string CustomerId) : Event;
