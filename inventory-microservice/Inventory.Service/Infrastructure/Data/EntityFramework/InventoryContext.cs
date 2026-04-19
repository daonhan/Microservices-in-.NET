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
    public DbSet<StockMovement> StockMovements { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new StockItemConfiguration());
        modelBuilder.ApplyConfiguration(new StockLevelConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
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

    public async Task<IReadOnlyList<StockItem>> ListStockItems()
    {
        return await StockItems
            .OrderBy(s => s.ProductId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<StockMovement>> GetMovements(int productId)
    {
        return await StockMovements
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
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

    public async Task<RestockResult?> Restock(int productId, int quantity)
    {
        var stockItem = await StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
        if (stockItem is null)
        {
            return null;
        }

        var defaultWarehouse = await Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        var stockLevel = await StockLevels
            .FirstOrDefaultAsync(l => l.ProductId == productId && l.WarehouseId == defaultWarehouse.Id);

        if (stockLevel is null)
        {
            stockLevel = new StockLevel
            {
                ProductId = productId,
                WarehouseId = defaultWarehouse.Id,
                OnHand = 0,
                Reserved = 0
            };
            StockLevels.Add(stockLevel);
        }

        stockLevel.OnHand += quantity;
        stockItem.TotalOnHand += quantity;

        StockMovements.Add(new StockMovement
        {
            ProductId = productId,
            WarehouseId = defaultWarehouse.Id,
            Type = MovementType.Restock,
            Quantity = quantity,
            OccurredAt = DateTime.UtcNow
        });

        await SaveChangesAsync();

        return new RestockResult(defaultWarehouse.Id, stockLevel.OnHand);
    }
}
