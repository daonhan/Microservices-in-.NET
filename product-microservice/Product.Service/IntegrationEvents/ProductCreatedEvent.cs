using ECommerce.Shared.Infrastructure.EventBus;

namespace Product.Service.IntegrationEvents;

public record ProductCreatedEvent(int ProductId, string Name, decimal Price) : Event;
