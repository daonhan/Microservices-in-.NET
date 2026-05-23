using Inventory.Service.Domain.Abstractions;

namespace Inventory.Service.Features.CreateBackorder;

internal sealed class CreateBackorderHandler
{
    private readonly IInventoryStore _inventoryStore;

    public CreateBackorderHandler(IInventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore;
    }

    public async Task<BackorderResponse?> HandleAsync(int productId, BackorderRequestDto request)
    {
        var result = await _inventoryStore.CreateBackorder(request.CustomerId, productId, request.Quantity);

        if (result is null)
        {
            return null;
        }

        return new BackorderResponse(
            result.Id,
            result.CustomerId,
            result.ProductId,
            result.Quantity,
            result.CreatedAt);
    }
}
