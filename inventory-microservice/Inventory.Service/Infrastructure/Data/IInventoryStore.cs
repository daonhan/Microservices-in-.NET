using Inventory.Service.Models;

namespace Inventory.Service.Infrastructure.Data;

internal interface IInventoryStore
{
    Task<StockItem?> GetStockItem(int productId);

    Task<IReadOnlyList<StockLevel>> GetStockLevels(int productId);

    Task<IReadOnlyList<StockItem>> ListStockItems();

    Task<IReadOnlyList<StockMovement>> GetMovements(int productId);

    Task ProvisionStockItem(int productId);

    Task<RestockResult?> Restock(int productId, int quantity);
}

internal record RestockResult(int WarehouseId, int NewOnHand);
