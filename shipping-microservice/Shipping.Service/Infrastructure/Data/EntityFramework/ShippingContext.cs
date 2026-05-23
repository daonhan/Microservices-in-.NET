using Microsoft.EntityFrameworkCore;
using Shipping.Service.Domain;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class ShippingContext : DbContext
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
}
