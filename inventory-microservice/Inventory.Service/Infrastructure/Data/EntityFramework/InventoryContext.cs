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
    public DbSet<StockReservation> StockReservations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new StockItemConfiguration());
        modelBuilder.ApplyConfiguration(new StockLevelConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
        modelBuilder.ApplyConfiguration(new StockReservationConfiguration());
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

        var availableBefore = stockItem.Available;

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

        return new RestockResult(
            defaultWarehouse.Id,
            stockLevel.OnHand,
            availableBefore,
            stockItem.Available,
            stockItem.LowStockThreshold);
    }

    public async Task<SetThresholdResult?> SetThreshold(int productId, int threshold)
    {
        var stockItem = await StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
        if (stockItem is null)
        {
            return null;
        }

        var defaultWarehouse = await Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        var thresholdBefore = stockItem.LowStockThreshold;
        stockItem.LowStockThreshold = threshold;

        await SaveChangesAsync();

        return new SetThresholdResult(
            defaultWarehouse.Id,
            stockItem.Available,
            thresholdBefore,
            threshold);
    }

    public async Task<ReserveResult> Reserve(Guid orderId, IReadOnlyList<ReserveLine> lines)
    {
        var existing = await StockReservations
            .Where(r => r.OrderId == orderId)
            .ToListAsync();

        if (existing.Count > 0)
        {
            var already = existing
                .Select(r => new ReservedLine(r.ProductId, r.WarehouseId, r.Quantity))
                .ToList();

            return new ReserveResult(Reserved: true, AlreadyProcessed: true, already, []);
        }

        var defaultWarehouse = await Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        var productIds = lines.Select(l => l.ProductId).ToArray();

        var stockItems = await StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);

        var stockLevels = await StockLevels
            .Where(l => productIds.Contains(l.ProductId) && l.WarehouseId == defaultWarehouse.Id)
            .ToDictionaryAsync(l => l.ProductId);

        var failedLines = new List<FailedReserveLine>();
        foreach (var line in lines)
        {
            if (!stockItems.TryGetValue(line.ProductId, out var item) ||
                !stockLevels.TryGetValue(line.ProductId, out _))
            {
                failedLines.Add(new FailedReserveLine(line.ProductId, line.Quantity, 0));
                continue;
            }

            if (item.Available < line.Quantity)
            {
                failedLines.Add(new FailedReserveLine(line.ProductId, line.Quantity, item.Available));
            }
        }

        if (failedLines.Count > 0)
        {
            return new ReserveResult(Reserved: false, AlreadyProcessed: false, [], failedLines);
        }

        var now = DateTime.UtcNow;
        var reservedLines = new List<ReservedLine>(lines.Count);

        foreach (var line in lines)
        {
            var item = stockItems[line.ProductId];
            var level = stockLevels[line.ProductId];

            level.Reserved += line.Quantity;
            item.TotalReserved += line.Quantity;

            StockReservations.Add(new StockReservation
            {
                OrderId = orderId,
                ProductId = line.ProductId,
                WarehouseId = defaultWarehouse.Id,
                Quantity = line.Quantity,
                Status = ReservationStatus.Held,
                CreatedAt = now
            });

            StockMovements.Add(new StockMovement
            {
                ProductId = line.ProductId,
                WarehouseId = defaultWarehouse.Id,
                Type = MovementType.Reserve,
                Quantity = line.Quantity,
                OccurredAt = now,
                OrderId = orderId
            });

            reservedLines.Add(new ReservedLine(line.ProductId, defaultWarehouse.Id, line.Quantity));
        }

        await SaveChangesAsync();

        return new ReserveResult(Reserved: true, AlreadyProcessed: false, reservedLines, []);
    }

    public async Task<CommitResult> CommitReservations(Guid orderId)
    {
        var reservations = await StockReservations
            .Where(r => r.OrderId == orderId)
            .ToListAsync();

        if (reservations.Count == 0)
        {
            return new CommitResult(Committed: false, AlreadyProcessed: false, []);
        }

        if (reservations.All(r => r.Status != ReservationStatus.Held))
        {
            var already = reservations
                .Where(r => r.Status == ReservationStatus.Committed)
                .Select(r => new CommittedLine(r.ProductId, r.WarehouseId, r.Quantity))
                .ToList();
            return new CommitResult(Committed: true, AlreadyProcessed: true, already);
        }

        var productIds = reservations.Select(r => r.ProductId).Distinct().ToArray();

        var stockItems = await StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);

        var stockLevels = await StockLevels
            .Where(l => productIds.Contains(l.ProductId))
            .ToListAsync();
        var stockLevelsByKey = stockLevels.ToDictionary(l => (l.ProductId, l.WarehouseId));

        var now = DateTime.UtcNow;
        var committedLines = new List<CommittedLine>();

        foreach (var reservation in reservations)
        {
            if (reservation.Status != ReservationStatus.Held)
            {
                continue;
            }

            var item = stockItems[reservation.ProductId];
            var level = stockLevelsByKey[(reservation.ProductId, reservation.WarehouseId)];

            level.Reserved -= reservation.Quantity;
            level.OnHand -= reservation.Quantity;
            item.TotalReserved -= reservation.Quantity;
            item.TotalOnHand -= reservation.Quantity;

            reservation.Status = ReservationStatus.Committed;

            StockMovements.Add(new StockMovement
            {
                ProductId = reservation.ProductId,
                WarehouseId = reservation.WarehouseId,
                Type = MovementType.Commit,
                Quantity = reservation.Quantity,
                OccurredAt = now,
                OrderId = orderId
            });

            committedLines.Add(new CommittedLine(reservation.ProductId, reservation.WarehouseId, reservation.Quantity));
        }

        await SaveChangesAsync();

        return new CommitResult(Committed: true, AlreadyProcessed: false, committedLines);
    }

    public async Task<ReleaseResult> ReleaseReservations(Guid orderId)
    {
        var reservations = await StockReservations
            .Where(r => r.OrderId == orderId)
            .ToListAsync();

        if (reservations.Count == 0)
        {
            return new ReleaseResult(Released: false, AlreadyProcessed: false, []);
        }

        if (reservations.All(r => r.Status == ReservationStatus.Released))
        {
            var already = reservations
                .Select(r => new ReleasedLine(r.ProductId, r.WarehouseId, r.Quantity))
                .ToList();
            return new ReleaseResult(Released: true, AlreadyProcessed: true, already);
        }

        var productIds = reservations.Select(r => r.ProductId).Distinct().ToArray();

        var stockItems = await StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);

        var stockLevels = await StockLevels
            .Where(l => productIds.Contains(l.ProductId))
            .ToListAsync();
        var stockLevelsByKey = stockLevels.ToDictionary(l => (l.ProductId, l.WarehouseId));

        var now = DateTime.UtcNow;
        var releasedLines = new List<ReleasedLine>();

        foreach (var reservation in reservations)
        {
            if (reservation.Status == ReservationStatus.Released)
            {
                continue;
            }

            var item = stockItems[reservation.ProductId];
            var level = stockLevelsByKey[(reservation.ProductId, reservation.WarehouseId)];

            if (reservation.Status == ReservationStatus.Held)
            {
                level.Reserved -= reservation.Quantity;
                item.TotalReserved -= reservation.Quantity;
            }
            else if (reservation.Status == ReservationStatus.Committed)
            {
                level.OnHand += reservation.Quantity;
                item.TotalOnHand += reservation.Quantity;
            }

            reservation.Status = ReservationStatus.Released;

            StockMovements.Add(new StockMovement
            {
                ProductId = reservation.ProductId,
                WarehouseId = reservation.WarehouseId,
                Type = MovementType.Release,
                Quantity = reservation.Quantity,
                OccurredAt = now,
                OrderId = orderId
            });

            releasedLines.Add(new ReleasedLine(reservation.ProductId, reservation.WarehouseId, reservation.Quantity));
        }

        await SaveChangesAsync();

        return new ReleaseResult(Released: true, AlreadyProcessed: false, releasedLines);
    }
}
