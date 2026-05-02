using ECommerce.Shared.Infrastructure.EventBus;

namespace Order.Service.IntegrationEvents.Events;

public record ProductCreatedEvent(int ProductId, string Name, decimal Price) : Event;
