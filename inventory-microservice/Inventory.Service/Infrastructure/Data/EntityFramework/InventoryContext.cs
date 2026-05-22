using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class InventoryContext : DbContext, IInventoryStore
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

    private void RecordStockMovement(StockMovement movement)
    {
        StockMovements.Add(movement);
        _metricFactory.Counter("stock-movements", "movements")
            .Add(1, new KeyValuePair<string, object?>("movement_type", movement.Type.ToString()));
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

        var now = DateTime.UtcNow;

        RecordStockMovement(new StockMovement
        {
            ProductId = productId,
            WarehouseId = defaultWarehouse.Id,
            Type = MovementType.Restock,
            Quantity = quantity,
            OccurredAt = now
        });

        var pending = await BackorderRequests
            .Where(b => b.ProductId == productId && b.FulfilledAt == null)
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .ToListAsync();

        var fulfilled = new List<FulfilledBackorder>();
        var remaining = stockItem.Available;
        foreach (var request in pending)
        {
            if (remaining < request.Quantity)
            {
                break;
            }

            request.FulfilledAt = now;
            remaining -= request.Quantity;
            fulfilled.Add(new FulfilledBackorder(request.Id, request.CustomerId, request.Quantity));
        }

        await SaveChangesAsync();

        return new RestockResult(
            defaultWarehouse.Id,
            stockLevel.OnHand,
            availableBefore,
            stockItem.Available,
            stockItem.LowStockThreshold,
            fulfilled);
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
        if (lines.Count == 0)
        {
            return new ReserveResult(Reserved: false, AlreadyProcessed: false, [], []);
        }

        var existingReservations = await StockReservations
            .Where(r => r.OrderId == orderId)
            .ToListAsync();

        var defaultWarehouse = await Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        var productIds = lines.Select(l => l.ProductId).ToArray();

        var stockItems = await StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);

        var stockLevels = await StockLevels
            .Where(l => productIds.Contains(l.ProductId) && l.WarehouseId == defaultWarehouse.Id)
            .ToDictionaryAsync(l => l.ProductId);

        var now = DateTime.UtcNow;
        var failedLines = new List<FailedReserveLine>();
        var plans = new List<(HoldResult Plan, StockItem Item, StockLevel Level)>(lines.Count);
        var pendingByProduct = new Dictionary<int, int>();
        bool alreadyProcessed = false;

        foreach (var line in lines)
        {
            if (!stockItems.TryGetValue(line.ProductId, out var item) ||
                !stockLevels.TryGetValue(line.ProductId, out var level))
            {
                failedLines.Add(new FailedReserveLine(line.ProductId, line.Quantity, 0));
                continue;
            }

            pendingByProduct.TryGetValue(line.ProductId, out var pending);
            var plan = item.EvaluateHold(orderId, defaultWarehouse.Id, line.Quantity, existingReservations, pending, now);

            if (plan.Outcome == HoldOutcome.AlreadyHeld)
            {
                alreadyProcessed = true;
                continue;
            }

            if (plan.Outcome == HoldOutcome.InsufficientStock)
            {
                failedLines.Add(new FailedReserveLine(line.ProductId, line.Quantity, plan.Available));
                continue;
            }

            pendingByProduct[line.ProductId] = pending + line.Quantity;
            plans.Add((plan, item, level));
        }

        if (alreadyProcessed)
        {
            var already = existingReservations
                .Select(r => new ReservedLine(r.ProductId, r.WarehouseId, r.Quantity))
                .ToList();
            return new ReserveResult(Reserved: true, AlreadyProcessed: true, already, []);
        }

        if (failedLines.Count > 0)
        {
            return new ReserveResult(Reserved: false, AlreadyProcessed: false, [], failedLines);
        }

        var reservedLines = new List<ReservedLine>(plans.Count);
        foreach (var (plan, item, level) in plans)
        {
            item.ApplyHold(plan, level);
            StockReservations.Add(plan.Reservation!);
            RecordStockMovement(plan.Movement!);
            reservedLines.Add(new ReservedLine(plan.ProductId, plan.WarehouseId, plan.Quantity));
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

        var productIds = reservations.Select(r => r.ProductId).Distinct().ToArray();

        var stockItems = await StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);

        var stockLevels = await StockLevels
            .Where(l => productIds.Contains(l.ProductId))
            .ToListAsync();

        var levelsByProduct = stockLevels
            .GroupBy(l => l.ProductId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<int, StockLevel>)g.ToDictionary(l => l.WarehouseId));

        var now = DateTime.UtcNow;
        var committedLines = new List<CommittedLine>();

        foreach (var group in reservations.GroupBy(r => r.ProductId))
        {
            var item = stockItems[group.Key];
            var levelsByWarehouse = levelsByProduct[group.Key];

            var result = item.Commit(orderId, group.ToList(), levelsByWarehouse, now);
            foreach (var movement in result.Movements)
            {
                RecordStockMovement(movement);
                committedLines.Add(new CommittedLine(movement.ProductId, movement.WarehouseId, movement.Quantity));
            }
        }

        if (committedLines.Count == 0)
        {
            var already = reservations
                .Where(r => r.Status == ReservationStatus.Committed)
                .Select(r => new CommittedLine(r.ProductId, r.WarehouseId, r.Quantity))
                .ToList();
            return new CommitResult(Committed: true, AlreadyProcessed: true, already);
        }

        await SaveChangesAsync();

        return new CommitResult(Committed: true, AlreadyProcessed: false, committedLines);
    }

    public async Task<BackorderResult?> CreateBackorder(string customerId, int productId, int quantity)
    {
        var stockItem = await StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
        if (stockItem is null)
        {
            return null;
        }

        var request = new BackorderRequest
        {
            CustomerId = customerId,
            ProductId = productId,
            Quantity = quantity,
            CreatedAt = DateTime.UtcNow,
            FulfilledAt = null
        };

        BackorderRequests.Add(request);

        await SaveChangesAsync();

        return new BackorderResult(request.Id, request.CustomerId, request.ProductId, request.Quantity, request.CreatedAt);
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

        var levelsByProduct = stockLevels
            .GroupBy(l => l.ProductId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<int, StockLevel>)g.ToDictionary(l => l.WarehouseId));

        var now = DateTime.UtcNow;
        var releasedLines = new List<ReleasedLine>();

        foreach (var group in reservations.GroupBy(r => r.ProductId))
        {
            var item = stockItems[group.Key];
            var levelsByWarehouse = levelsByProduct[group.Key];

            var result = item.Release(orderId, group.ToList(), levelsByWarehouse, now);
            foreach (var movement in result.Movements)
            {
                RecordStockMovement(movement);
                releasedLines.Add(new ReleasedLine(movement.ProductId, movement.WarehouseId, movement.Quantity));
            }
        }

        await SaveChangesAsync();

        return new ReleaseResult(Released: true, AlreadyProcessed: false, releasedLines);
    }
}
