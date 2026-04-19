using Inventory.Service.Models;

namespace Inventory.Service.Infrastructure.Data;

internal interface IInventoryStore
{
    Task<StockItem?> GetStockItem(int productId);

    Task<IReadOnlyList<StockLevel>> GetStockLevels(int productId);
}
