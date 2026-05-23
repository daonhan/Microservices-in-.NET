using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.Contracts.Integration;

public record ProductCreatedEvent(int ProductId, string Name, decimal Price) : Event;
