using Microsoft.EntityFrameworkCore;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class EfShipmentStore : IShipmentStore
{
    private readonly ShippingContext _ctx;

    public EfShipmentStore(ShippingContext ctx)
    {
        _ctx = ctx;
    }

    public async Task<IReadOnlyList<Shipment>> GetByOrder(Guid orderId)
    {
        return await _ctx.Shipments
            .Include(s => s.Lines)
            .Where(s => s.OrderId == orderId)
            .OrderBy(s => s.WarehouseId)
            .ToListAsync();
    }

    public async Task<Shipment?> GetById(Guid shipmentId)
    {
        return await _ctx.Shipments
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
    }

    public async Task<IReadOnlyList<Shipment>> ListShipments(ShipmentListFilters filters)
    {
        var query = _ctx.Shipments
            .Include(s => s.Lines)
            .AsQueryable();

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
            .ToListAsync();
    }

    public Task<int> SaveChangesAsync() => _ctx.SaveChangesAsync();

    public async Task<IReadOnlyList<Shipment>> ListActiveShipments()
    {
        return await _ctx.Shipments
            .Include(s => s.Lines)
            .Where(s => s.Status == ShipmentStatus.Shipped || s.Status == ShipmentStatus.InTransit)
            .ToListAsync();
    }

    public async Task<Shipment?> GetByTrackingNumber(string trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return null;
        }

        return await _ctx.Shipments
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber);
    }

    public async Task<CreateShipmentsResult> CreateShipmentsForOrder(
        Guid orderId,
        string customerId,
        IReadOnlyList<CreateShipmentLine> lines)
    {
        var existing = await _ctx.Shipments
            .AnyAsync(s => s.OrderId == orderId);

        if (existing)
        {
            return new CreateShipmentsResult(Created: false, Shipments: []);
        }

        var now = DateTime.UtcNow;
        var shipments = new List<Shipment>();

        foreach (var group in lines.GroupBy(l => l.WarehouseId))
        {
            var shipment = Shipment.Create(
                id: Guid.NewGuid(),
                orderId: orderId,
                customerId: customerId,
                warehouseId: group.Key,
                createdAt: now);

            foreach (var line in group)
            {
                shipment.AddLine(line.ProductId, line.Quantity);
            }

            _ctx.Shipments.Add(shipment);
            shipments.Add(shipment);
        }

        await _ctx.SaveChangesAsync();

        return new CreateShipmentsResult(Created: true, Shipments: shipments);
    }

    public async Task RecordOrderConfirmation(Guid orderId, string customerId)
    {
        var exists = await _ctx.OrderConfirmations.AnyAsync(o => o.OrderId == orderId);
        if (exists)
        {
            return;
        }

        _ctx.OrderConfirmations.Add(new OrderConfirmation
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });

        await _ctx.SaveChangesAsync();
    }

    public async Task<string?> TryGetOrderCustomer(Guid orderId)
    {
        var confirmation = await _ctx.OrderConfirmations
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

        return confirmation?.CustomerId;
    }
}
