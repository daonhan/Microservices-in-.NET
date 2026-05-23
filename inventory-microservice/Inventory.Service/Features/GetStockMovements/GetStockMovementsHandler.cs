using Inventory.Service.Infrastructure.Data.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Features.GetStockMovements;

internal sealed class GetStockMovementsHandler
{
    private readonly InventoryContext _context;

    public GetStockMovementsHandler(InventoryContext context)
    {
        _context = context;
    }

    public async Task<List<StockMovementDto>> HandleAsync(int productId)
    {
        return await _context.StockMovements
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .Select(m => new StockMovementDto(
                m.Id,
                m.ProductId,
                m.WarehouseId,
                m.Type.ToString(),
                m.Quantity,
                m.OccurredAt,
                m.OrderId,
                m.Reason))
            .ToListAsync();
    }
}
