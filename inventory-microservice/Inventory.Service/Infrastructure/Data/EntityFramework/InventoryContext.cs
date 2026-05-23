using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class InventoryContext : DbContext
{
    private readonly MetricFactory _metricFactory;

    public InventoryContext(DbContextOptions<InventoryContext> options, MetricFactory metricFactory)
        : base(options)
    {
        _metricFactory = metricFactory;
    }

    public DbSet<Warehouse> Warehouses { get; set; } = null!;
    public DbSet<StockItem> StockItems { get; set; } = null!;
    public DbSet<StockLevel> StockLevels { get; set; } = null!;
    public DbSet<StockMovement> StockMovements { get; set; } = null!;
    public DbSet<StockReservation> StockReservations { get; set; } = null!;
    public DbSet<BackorderRequest> BackorderRequests { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new StockItemConfiguration());
        modelBuilder.ApplyConfiguration(new StockLevelConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
        modelBuilder.ApplyConfiguration(new StockReservationConfiguration());
        modelBuilder.ApplyConfiguration(new BackorderRequestConfiguration());
    }

    internal void RecordStockMovement(StockMovement movement)
    {
        StockMovements.Add(movement);
        _metricFactory.Counter("stock-movements", "movements")
            .Add(1, new KeyValuePair<string, object?>("movement_type", movement.Type.ToString()));
    }
}
