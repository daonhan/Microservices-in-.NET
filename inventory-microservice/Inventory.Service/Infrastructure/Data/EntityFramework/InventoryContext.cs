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
}
