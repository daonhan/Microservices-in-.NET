using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class InventoryContext : DbContext, IInventoryStore
{
    public InventoryContext(DbContextOptions<InventoryContext> options)
        : base(options)
    {
    }

    public DbSet<Warehouse> Warehouses { get; set; } = null!;
    public DbSet<StockItem> StockItems { get; set; } = null!;
    public DbSet<StockLevel> StockLevels { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new StockItemConfiguration());
        modelBuilder.ApplyConfiguration(new StockLevelConfiguration());
    }

    public async Task<StockItem?> GetStockItem(int productId)
    {
        return await StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
    }

    public async Task<IReadOnlyList<StockLevel>> GetStockLevels(int productId)
    {
        return await StockLevels
            .Include(l => l.Warehouse)
            .Where(l => l.ProductId == productId)
            .ToListAsync();
    }

    public async Task ProvisionStockItem(int productId)
    {
        var existing = await StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
        if (existing is not null)
        {
            return;
        }

        var defaultWarehouse = await Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 0,
            TotalReserved = 0,
            LowStockThreshold = 0
        });

        StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = defaultWarehouse.Id,
            OnHand = 0,
            Reserved = 0
        });

        await SaveChangesAsync();
    }
}
