using Microsoft.EntityFrameworkCore;
using Shipping.Service.Models;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class ShippingContext : DbContext, IShipmentStore
{
    public ShippingContext(DbContextOptions<ShippingContext> options)
        : base(options)
    {
    }

    public DbSet<Warehouse> Warehouses { get; set; } = null!;
    public DbSet<Shipment> Shipments { get; set; } = null!;
    public DbSet<ShipmentLine> ShipmentLines { get; set; } = null!;
    public DbSet<ShipmentStatusHistoryEntry> ShipmentStatusHistory { get; set; } = null!;
    public DbSet<OrderConfirmation> OrderConfirmations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentLineConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentStatusHistoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfirmationConfiguration());
    }

    public async Task<IReadOnlyList<Shipment>> GetByOrder(Guid orderId)
    {
        return await Shipments
            .Include(s => s.Lines)
            .Where(s => s.OrderId == orderId)
            .OrderBy(s => s.WarehouseId)
            .ToListAsync();
    }

    public async Task<Shipment?> GetById(Guid shipmentId)
    {
        return await Shipments
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
    }

    public async Task<CreateShipmentsResult> CreateShipmentsForOrder(
        Guid orderId,
        string customerId,
        IReadOnlyList<CreateShipmentLine> lines)
    {
        var existing = await Shipments
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

            Shipments.Add(shipment);
            shipments.Add(shipment);
        }

        await SaveChangesAsync();

        return new CreateShipmentsResult(Created: true, Shipments: shipments);
    }

    public async Task RecordOrderConfirmation(Guid orderId, string customerId)
    {
        var exists = await OrderConfirmations.AnyAsync(o => o.OrderId == orderId);
        if (exists)
        {
            return;
        }

        OrderConfirmations.Add(new OrderConfirmation
        {
            OrderId = orderId,
            CustomerId = customerId,
            ReceivedAt = DateTime.UtcNow,
        });

        await SaveChangesAsync();
    }

    public async Task<string?> TryGetOrderCustomer(Guid orderId)
    {
        var confirmation = await OrderConfirmations
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

        return confirmation?.CustomerId;
    }
}
