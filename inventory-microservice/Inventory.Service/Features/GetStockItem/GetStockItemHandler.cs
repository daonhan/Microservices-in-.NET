using Inventory.Service.Infrastructure.Data.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Features.GetStockItem;

internal sealed class GetStockItemHandler
{
    private readonly InventoryContext _context;

    public GetStockItemHandler(InventoryContext context)
    {
        _context = context;
    }

    public async Task<GetStockItemResponse?> HandleAsync(int productId)
    {
        var summary = await _context.StockItems
            .Where(s => s.ProductId == productId)
            .Select(s => new
            {
                s.ProductId,
                s.TotalOnHand,
                s.TotalReserved,
                s.LowStockThreshold,
            })
            .FirstOrDefaultAsync();

        if (summary is null)
        {
            return null;
        }

        var perWarehouse = await _context.StockLevels
            .Where(l => l.ProductId == productId)
            .Select(l => new StockLevelDto(
                l.WarehouseId,
                l.Warehouse != null ? l.Warehouse.Code : string.Empty,
                l.OnHand,
                l.Reserved))
            .ToListAsync();

        return new GetStockItemResponse(
            summary.ProductId,
            summary.TotalOnHand,
            summary.TotalReserved,
            summary.TotalOnHand - summary.TotalReserved,
            summary.LowStockThreshold,
            perWarehouse);
    }
}
