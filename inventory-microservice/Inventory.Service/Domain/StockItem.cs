namespace Inventory.Service.Domain;

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
        var plan = EvaluateHold(orderId, warehouseId, quantity, orderReservations, pendingHoldQuantity: 0, timestamp);
        ApplyHold(plan, level);
        return plan;
    }

    /// <summary>
    /// Computes the outcome of a prospective hold without mutating state. Multi-line callers
    /// can use this to validate every line before any mutation happens, so a single failed
    /// line doesn't leave the aggregate or its <see cref="StockLevel"/> partially modified.
    /// <paramref name="pendingHoldQuantity"/> is the sum of quantities the caller has already
    /// planned (but not yet applied) for this same product in the current operation, so that
    /// repeated lines for one product correctly account for cumulative consumption.
    /// </summary>
    public HoldResult EvaluateHold(
        Guid orderId,
        int warehouseId,
        int quantity,
        IReadOnlyCollection<StockReservation> orderReservations,
        int pendingHoldQuantity,
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

        var effectiveAvailable = Available - pendingHoldQuantity;
        if (effectiveAvailable < quantity)
        {
            return new HoldResult(
                HoldOutcome.InsufficientStock,
                ProductId,
                warehouseId,
                quantity,
                effectiveAvailable,
                Reservation: null,
                Movement: null);
        }

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
            effectiveAvailable - quantity,
            reservation,
            movement);
    }

    /// <summary>
    /// Applies a previously evaluated hold by mutating <see cref="TotalReserved"/> and the
    /// per-warehouse <see cref="StockLevel.Reserved"/> together. Non-<see cref="HoldOutcome.Held"/>
    /// plans are a no-op so callers can apply uniformly.
    /// </summary>
    public void ApplyHold(HoldResult plan, StockLevel level)
    {
        if (plan.Outcome != HoldOutcome.Held)
        {
            return;
        }

        level.Reserved += plan.Quantity;
        TotalReserved += plan.Quantity;
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

            reservation.Commit();

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

    /// <summary>
    /// Releases this order's reservations. Releasing a <see cref="ReservationStatus.Held"/>
    /// reservation only returns the reserved units (<see cref="TotalReserved"/> and the
    /// per-warehouse <see cref="StockLevel.Reserved"/> are decremented together); releasing a
    /// <see cref="ReservationStatus.Committed"/> reservation also restores on-hand stock
    /// (<see cref="TotalOnHand"/> and <see cref="StockLevel.OnHand"/> are incremented together).
    /// Mixed held/committed orders are handled in one call. Already-released reservations are
    /// skipped, so a double-release is an idempotent no-op reported as
    /// <see cref="ReleaseOutcome.NothingToRelease"/>.
    /// </summary>
    public ReleaseItemResult Release(
        Guid orderId,
        IReadOnlyCollection<StockReservation> orderReservations,
        IReadOnlyDictionary<int, StockLevel> levelsByWarehouse,
        DateTime timestamp)
    {
        var movements = new List<StockMovement>();

        foreach (var reservation in orderReservations)
        {
            if (reservation.Status == ReservationStatus.Released)
            {
                continue;
            }

            var level = levelsByWarehouse[reservation.WarehouseId];

            if (reservation.Status == ReservationStatus.Held)
            {
                level.Reserved -= reservation.Quantity;
                TotalReserved -= reservation.Quantity;
            }
            else if (reservation.Status == ReservationStatus.Committed)
            {
                level.OnHand += reservation.Quantity;
                TotalOnHand += reservation.Quantity;
            }

            reservation.Release();

            movements.Add(new StockMovement
            {
                ProductId = ProductId,
                WarehouseId = reservation.WarehouseId,
                Type = MovementType.Release,
                Quantity = reservation.Quantity,
                OccurredAt = timestamp,
                OrderId = orderId
            });
        }

        return new ReleaseItemResult(
            movements.Count > 0 ? ReleaseOutcome.Released : ReleaseOutcome.NothingToRelease,
            movements);
    }
}
