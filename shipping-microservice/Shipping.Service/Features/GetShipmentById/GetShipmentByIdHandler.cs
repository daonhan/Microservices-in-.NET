using Microsoft.EntityFrameworkCore;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Service.Infrastructure.Data.EntityFramework;

namespace Shipping.Service.Features.GetShipmentById;

internal sealed class GetShipmentByIdHandler
{
    private readonly ShippingContext _context;

    public GetShipmentByIdHandler(ShippingContext context)
    {
        _context = context;
    }

    public async Task<ShipmentResponse?> HandleAsync(Guid shipmentId)
    {
        return await _context.Shipments
            .Where(s => s.Id == shipmentId)
            .Select(s => new ShipmentResponse(
                s.Id,
                s.OrderId,
                s.CustomerId,
                s.WarehouseId,
                s.Status.ToString(),
                s.CreatedAt,
                s.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList()))
            .FirstOrDefaultAsync();
    }
}
