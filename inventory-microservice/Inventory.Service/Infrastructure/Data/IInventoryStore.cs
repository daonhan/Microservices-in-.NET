using Inventory.Service.Models;

namespace Inventory.Service.Infrastructure.Data;

internal interface IInventoryStore
{
    Task<StockItem?> GetStockItem(int productId);

    Task<IReadOnlyList<StockLevel>> GetStockLevels(int productId);

    Task<IReadOnlyList<StockItem>> ListStockItems();

    Task<IReadOnlyList<StockMovement>> GetMovements(int productId);

    Task ProvisionStockItem(int productId);

    Task<RestockResult?> Restock(int productId, int quantity);

    Task<SetThresholdResult?> SetThreshold(int productId, int threshold);

    Task<ReserveResult> Reserve(Guid orderId, IReadOnlyList<ReserveLine> lines);

    Task<CommitResult> CommitReservations(Guid orderId);

    Task<ReleaseResult> ReleaseReservations(Guid orderId);
}

internal record RestockResult(
    int WarehouseId,
    int NewOnHand,
    int AvailableBefore,
    int AvailableAfter,
    int Threshold);

internal record SetThresholdResult(
    int WarehouseId,
    int Available,
    int ThresholdBefore,
    int ThresholdAfter);

internal record ReserveLine(int ProductId, int Quantity);

internal record ReservedLine(int ProductId, int WarehouseId, int Quantity);

internal record FailedReserveLine(int ProductId, int Requested, int Available);

internal record ReserveResult(
    bool Reserved,
    bool AlreadyProcessed,
    IReadOnlyList<ReservedLine> Lines,
    IReadOnlyList<FailedReserveLine> FailedLines);

internal record CommittedLine(int ProductId, int WarehouseId, int Quantity);

internal record CommitResult(bool Committed, bool AlreadyProcessed, IReadOnlyList<CommittedLine> Lines);

internal record ReleasedLine(int ProductId, int WarehouseId, int Quantity);

internal record ReleaseResult(bool Released, bool AlreadyProcessed, IReadOnlyList<ReleasedLine> Lines);
