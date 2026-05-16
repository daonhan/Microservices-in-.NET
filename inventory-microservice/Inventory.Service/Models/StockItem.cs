namespace Inventory.Service.Models;

internal class StockItem
{
    public int ProductId { get; set; }

    public int TotalOnHand { get; set; }

    public int TotalReserved { get; set; }

    public int LowStockThreshold { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public int Available => TotalOnHand - TotalReserved;

    /// <summary>
    /// Holds <paramref name="quantity"/> units of this item against an order. The
    /// aggregate owns the reservation invariants: an order that already holds stock
    /// short-circuits idempotently, and a hold can never exceed <see cref="Available"/>.
    /// <see cref="TotalReserved"/> and the per-warehouse <see cref="StockLevel.Reserved"/>
    /// are mutated together so they cannot drift apart.
    /// </summary>
    public HoldResult Hold(
        Guid orderId,
        int warehouseId,
        int quantity,
        StockLevel level,
        IReadOnlyCollection<StockReservation> orderReservations,
        DateTime timestamp)
    {
        var existing = orderReservations.FirstOrDefault(r => r.OrderId == orderId);
        if (existing is not null)
        {
            return new HoldResult(
                HoldOutcome.AlreadyHeld,
                ProductId,
                existing.WarehouseId,
                existing.Quantity,
                Available,
                Reservation: null,
                Movement: null);
        }

        if (Available < quantity)
        {
            return new HoldResult(
                HoldOutcome.InsufficientStock,
                ProductId,
                warehouseId,
                quantity,
                Available,
                Reservation: null,
                Movement: null);
        }

        level.Reserved += quantity;
        TotalReserved += quantity;

        var reservation = new StockReservation
        {
            OrderId = orderId,
            ProductId = ProductId,
            WarehouseId = warehouseId,
            Quantity = quantity,
            Status = ReservationStatus.Held,
            CreatedAt = timestamp
        };

        var movement = new StockMovement
        {
            ProductId = ProductId,
            WarehouseId = warehouseId,
            Type = MovementType.Reserve,
            Quantity = quantity,
            OccurredAt = timestamp,
            OrderId = orderId
        };

        return new HoldResult(
            HoldOutcome.Held,
            ProductId,
            warehouseId,
            quantity,
            Available,
            reservation,
            movement);
    }
}
