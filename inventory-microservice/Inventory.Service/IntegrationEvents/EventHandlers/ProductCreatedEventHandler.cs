using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Inventory.Service.Infrastructure.Data;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal class ProductCreatedEventHandler : IEventHandler<ProductCreatedEvent>
{
    private readonly IInventoryStore _inventoryStore;

    public ProductCreatedEventHandler(IInventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore;
    }

    public async Task Handle(ProductCreatedEvent @event)
    {
        await _inventoryStore.ProvisionStockItem(@event.ProductId);
    }
}
