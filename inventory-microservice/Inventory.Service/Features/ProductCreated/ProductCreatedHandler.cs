using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Domain.Abstractions;

namespace Inventory.Service.Features.ProductCreated;

internal sealed class ProductCreatedHandler : IEventHandler<ProductCreatedEvent>
{
    private readonly IInventoryStore _inventoryStore;

    public ProductCreatedHandler(IInventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore;
    }

    public async Task Handle(ProductCreatedEvent @event)
    {
        await _inventoryStore.ProvisionStockItem(@event.ProductId);
    }
}
