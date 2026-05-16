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

    /// <summary>
    /// Commits this order's held reservations: the reserved units leave on-hand stock, so
    /// <see cref="TotalReserved"/>/<see cref="TotalOnHand"/> and the matching per-warehouse
    /// <see cref="StockLevel.Reserved"/>/<see cref="StockLevel.OnHand"/> are decremented together.
    /// Reservations that are not <see cref="ReservationStatus.Held"/> are skipped, so a
    /// double-commit (nothing left in <c>Held</c>) is an idempotent no-op reported as
    /// <see cref="CommitOutcome.NothingHeld"/>.
    /// </summary>
    public CommitItemResult Commit(
        Guid orderId,
        IReadOnlyCollection<StockReservation> orderReservations,
        IReadOnlyDictionary<int, StockLevel> levelsByWarehouse,
        DateTime timestamp)
    {
        var movements = new List<StockMovement>();

        foreach (var reservation in orderReservations)
        {
            if (reservation.Status != ReservationStatus.Held)
            {
                continue;
            }

            var level = levelsByWarehouse[reservation.WarehouseId];

            level.Reserved -= reservation.Quantity;
            level.OnHand -= reservation.Quantity;
            TotalReserved -= reservation.Quantity;
            TotalOnHand -= reservation.Quantity;

            reservation.Status = ReservationStatus.Committed;

            movements.Add(new StockMovement
            {
                ProductId = ProductId,
                WarehouseId = reservation.WarehouseId,
                Type = MovementType.Commit,
                Quantity = reservation.Quantity,
                OccurredAt = timestamp,
                OrderId = orderId
            });
        }

        return new CommitItemResult(
            movements.Count > 0 ? CommitOutcome.Committed : CommitOutcome.NothingHeld,
            movements);
    }
}
