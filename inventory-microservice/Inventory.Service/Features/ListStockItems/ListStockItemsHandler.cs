using Inventory.Service.Infrastructure.Data.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Features.ListStockItems;

internal sealed class ListStockItemsHandler
{
    private readonly InventoryContext _context;

    public ListStockItemsHandler(InventoryContext context)
    {
        _context = context;
    }

    public async Task<List<StockItemSummaryDto>> HandleAsync()
    {
        return await _context.StockItems
            .OrderBy(s => s.ProductId)
            .Select(s => new StockItemSummaryDto(
                s.ProductId,
                s.TotalOnHand,
                s.TotalReserved,
                s.TotalOnHand - s.TotalReserved,
                s.LowStockThreshold))
            .ToListAsync();
    }
}
