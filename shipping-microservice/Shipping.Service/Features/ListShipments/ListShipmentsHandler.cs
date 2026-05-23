using Microsoft.EntityFrameworkCore;
using Shipping.Service.Domain;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Service.Infrastructure.Data.EntityFramework;

namespace Shipping.Service.Features.ListShipments;

internal sealed class ListShipmentsHandler
{
    private readonly ShippingContext _context;

    public ListShipmentsHandler(ShippingContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ShipmentResponse>> HandleAsync(ListShipmentsFilters filters)
    {
        var query = _context.Shipments.AsQueryable();

        if (filters.Status is not null)
        {
            query = query.Where(s => s.Status == filters.Status);
        }

        if (filters.WarehouseId is not null)
        {
            query = query.Where(s => s.WarehouseId == filters.WarehouseId);
        }

        if (filters.From is not null)
        {
            query = query.Where(s => s.CreatedAt >= filters.From);
        }

        if (filters.To is not null)
        {
            query = query.Where(s => s.CreatedAt <= filters.To);
        }

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip(filters.Skip)
            .Take(filters.Take)
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

internal record ListShipmentsFilters(
    ShipmentStatus? Status,
    int? WarehouseId,
    DateTime? From,
    DateTime? To,
    int Skip,
    int Take);
