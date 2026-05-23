using Microsoft.EntityFrameworkCore;
using Shipping.Service.Infrastructure.Data.EntityFramework;

namespace Shipping.Service.Features.GetShipmentsByOrder;

internal sealed class GetShipmentsByOrderHandler
{
    private readonly ShippingContext _context;

    public GetShipmentsByOrderHandler(ShippingContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ShipmentResponse>> HandleAsync(Guid orderId)
    {
        return await _context.Shipments
            .Where(s => s.OrderId == orderId)
            .OrderBy(s => s.WarehouseId)
            .Select(s => new ShipmentResponse(
                s.Id,
                s.OrderId,
                s.CustomerId,
                s.WarehouseId,
                s.Status.ToString(),
                s.CreatedAt,
                s.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList()))
            .ToListAsync();
    }
}
