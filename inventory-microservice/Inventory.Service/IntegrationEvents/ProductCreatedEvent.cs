using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.IntegrationEvents;

public record ProductCreatedEvent(int ProductId, string Name, decimal Price) : Event;
