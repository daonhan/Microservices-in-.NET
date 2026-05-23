using Inventory.Service.Domain;
using Inventory.Service.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal sealed class EfInventoryStore : IInventoryStore
{
    private readonly InventoryContext _ctx;

    public EfInventoryStore(InventoryContext ctx)
    {
        _ctx = ctx;
    }

    public async Task ProvisionStockItem(int productId)
    {
        var existing = await _ctx.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
        if (existing is not null)
        {
            return;
        }

        var defaultWarehouse = await _ctx.Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        _ctx.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalOnHand = 0,
            TotalReserved = 0,
            LowStockThreshold = 0
        });

        _ctx.StockLevels.Add(new StockLevel
        {
            ProductId = productId,
            WarehouseId = defaultWarehouse.Id,
            OnHand = 0,
            Reserved = 0
        });

        await _ctx.SaveChangesAsync();
    }

    public async Task<RestockResult?> Restock(int productId, int quantity)
    {
        var stockItem = await _ctx.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
        if (stockItem is null)
        {
            return null;
        }

        var defaultWarehouse = await _ctx.Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        var stockLevel = await _ctx.StockLevels
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
            _ctx.StockLevels.Add(stockLevel);
        }

        var availableBefore = stockItem.Available;

        stockLevel.OnHand += quantity;
        stockItem.TotalOnHand += quantity;

        var now = DateTime.UtcNow;

        _ctx.RecordStockMovement(new StockMovement
        {
            ProductId = productId,
            WarehouseId = defaultWarehouse.Id,
            Type = MovementType.Restock,
            Quantity = quantity,
            OccurredAt = now
        });

        var pending = await _ctx.BackorderRequests
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

        await _ctx.SaveChangesAsync();

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
        var stockItem = await _ctx.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
        if (stockItem is null)
        {
            return null;
        }

        var defaultWarehouse = await _ctx.Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        var thresholdBefore = stockItem.LowStockThreshold;
        stockItem.LowStockThreshold = threshold;

        await _ctx.SaveChangesAsync();

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

        var existingReservations = await _ctx.StockReservations
            .Where(r => r.OrderId == orderId)
            .ToListAsync();

        var defaultWarehouse = await _ctx.Warehouses.FirstAsync(w => w.Code == "DEFAULT");

        var productIds = lines.Select(l => l.ProductId).ToArray();

        var stockItems = await _ctx.StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);

        var stockLevels = await _ctx.StockLevels
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
            _ctx.StockReservations.Add(plan.Reservation!);
            _ctx.RecordStockMovement(plan.Movement!);
            reservedLines.Add(new ReservedLine(plan.ProductId, plan.WarehouseId, plan.Quantity));
        }

        await _ctx.SaveChangesAsync();

        return new ReserveResult(Reserved: true, AlreadyProcessed: false, reservedLines, []);
    }

    public async Task<CommitResult> CommitReservations(Guid orderId)
    {
        var reservations = await _ctx.StockReservations
            .Where(r => r.OrderId == orderId)
            .ToListAsync();

        if (reservations.Count == 0)
        {
            return new CommitResult(Committed: false, AlreadyProcessed: false, []);
        }

        var productIds = reservations.Select(r => r.ProductId).Distinct().ToArray();

        var stockItems = await _ctx.StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);

        var stockLevels = await _ctx.StockLevels
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
                _ctx.RecordStockMovement(movement);
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

        await _ctx.SaveChangesAsync();

        return new CommitResult(Committed: true, AlreadyProcessed: false, committedLines);
    }

    public async Task<BackorderResult?> CreateBackorder(string customerId, int productId, int quantity)
    {
        var stockItem = await _ctx.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId);
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

        _ctx.BackorderRequests.Add(request);

        await _ctx.SaveChangesAsync();

        return new BackorderResult(request.Id, request.CustomerId, request.ProductId, request.Quantity, request.CreatedAt);
    }

    public async Task<ReleaseResult> ReleaseReservations(Guid orderId)
    {
        var reservations = await _ctx.StockReservations
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

        var stockItems = await _ctx.StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);

        var stockLevels = await _ctx.StockLevels
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
                _ctx.RecordStockMovement(movement);
                releasedLines.Add(new ReleasedLine(movement.ProductId, movement.WarehouseId, movement.Quantity));
            }
        }

        await _ctx.SaveChangesAsync();

        return new ReleaseResult(Released: true, AlreadyProcessed: false, releasedLines);
    }
}
