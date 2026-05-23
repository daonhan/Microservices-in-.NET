using Inventory.Service.Domain;

namespace Inventory.Tests.Domain;

public class StockItemTests
{
    [Fact]
    public void Available_IsOnHandMinusReserved()
    {
        var item = new StockItem
        {
            ProductId = 1,
            TotalOnHand = 10,
            TotalReserved = 3,
        };

        Assert.Equal(7, item.Available);
    }

    [Fact]
    public void Restock_IncrementsOnHand_AndAvailableIncreasesBySameAmount()
    {
        var item = new StockItem
        {
            ProductId = 1,
            TotalOnHand = 5,
            TotalReserved = 2,
        };

        item.TotalOnHand += 4;

        Assert.Equal(9, item.TotalOnHand);
        Assert.Equal(7, item.Available);
    }

    [Fact]
    public void Restock_WhenReservedExceedsOnHand_AvailableCanBeNegative()
    {
        var item = new StockItem
        {
            ProductId = 1,
            TotalOnHand = 2,
            TotalReserved = 5,
        };

        Assert.Equal(-3, item.Available);
    }

    [Fact]
    public void Hold_WithinAvailable_ReservesAndMutatesItemAndLevelTogether()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 10, TotalReserved = 0 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 10, Reserved = 0 };
        var orderId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

        var result = item.Hold(orderId, warehouseId: 7, quantity: 3, level, [], now);

        Assert.Equal(HoldOutcome.Held, result.Outcome);
        Assert.Equal(3, item.TotalReserved);
        Assert.Equal(3, level.Reserved);
        Assert.Equal(7, item.Available);

        Assert.NotNull(result.Reservation);
        Assert.Equal(orderId, result.Reservation!.OrderId);
        Assert.Equal(1, result.Reservation.ProductId);
        Assert.Equal(7, result.Reservation.WarehouseId);
        Assert.Equal(3, result.Reservation.Quantity);
        Assert.Equal(ReservationStatus.Held, result.Reservation.Status);
        Assert.Equal(now, result.Reservation.CreatedAt);

        Assert.NotNull(result.Movement);
        Assert.Equal(MovementType.Reserve, result.Movement!.Type);
        Assert.Equal(3, result.Movement.Quantity);
        Assert.Equal(orderId, result.Movement.OrderId);
        Assert.Equal(now, result.Movement.OccurredAt);
    }

    [Fact]
    public void Hold_BeyondAvailable_IsRejected_AndDoesNotMutate()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 2, TotalReserved = 0 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 2, Reserved = 0 };

        var result = item.Hold(Guid.NewGuid(), warehouseId: 7, quantity: 5, level, [], DateTime.UtcNow);

        Assert.Equal(HoldOutcome.InsufficientStock, result.Outcome);
        Assert.Equal(2, result.Available);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(0, level.Reserved);
        Assert.Null(result.Reservation);
        Assert.Null(result.Movement);
    }

    [Fact]
    public void Hold_WhenOrderAlreadyHeld_ShortCircuits_AndDoesNotMutate()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 10, TotalReserved = 4 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 10, Reserved = 4 };
        var orderId = Guid.NewGuid();
        var existing = new StockReservation
        {
            OrderId = orderId,
            ProductId = 1,
            WarehouseId = 7,
            Quantity = 4,
            Status = ReservationStatus.Held,
        };

        var result = item.Hold(orderId, warehouseId: 7, quantity: 3, level, [existing], DateTime.UtcNow);

        Assert.Equal(HoldOutcome.AlreadyHeld, result.Outcome);
        Assert.Equal(4, result.Quantity);
        Assert.Equal(4, item.TotalReserved);
        Assert.Equal(4, level.Reserved);
        Assert.Null(result.Reservation);
        Assert.Null(result.Movement);
    }

    [Fact]
    public void Commit_HeldReservation_DrawsReservedDownOutOfOnHand()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 10, TotalReserved = 4 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 10, Reserved = 4 };
        var orderId = Guid.NewGuid();
        var reservation = new StockReservation
        {
            OrderId = orderId,
            ProductId = 1,
            WarehouseId = 7,
            Quantity = 4,
            Status = ReservationStatus.Held,
        };
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

        var result = item.Commit(orderId, [reservation], new Dictionary<int, StockLevel> { [7] = level }, now);

        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        Assert.Equal(6, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(6, level.OnHand);
        Assert.Equal(0, level.Reserved);
        Assert.Equal(ReservationStatus.Committed, reservation.Status);

        var movement = Assert.Single(result.Movements);
        Assert.Equal(MovementType.Commit, movement.Type);
        Assert.Equal(1, movement.ProductId);
        Assert.Equal(7, movement.WarehouseId);
        Assert.Equal(4, movement.Quantity);
        Assert.Equal(orderId, movement.OrderId);
        Assert.Equal(now, movement.OccurredAt);
    }

    [Fact]
    public void Commit_WhenAlreadyCommitted_IsIdempotentNoOp()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 6, TotalReserved = 0 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 6, Reserved = 0 };
        var orderId = Guid.NewGuid();
        var reservation = new StockReservation
        {
            OrderId = orderId,
            ProductId = 1,
            WarehouseId = 7,
            Quantity = 4,
            Status = ReservationStatus.Committed,
        };

        var result = item.Commit(orderId, [reservation], new Dictionary<int, StockLevel> { [7] = level }, DateTime.UtcNow);

        Assert.Equal(CommitOutcome.NothingHeld, result.Outcome);
        Assert.Empty(result.Movements);
        Assert.Equal(6, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(6, level.OnHand);
        Assert.Equal(0, level.Reserved);
        Assert.Equal(ReservationStatus.Committed, reservation.Status);
    }

    [Fact]
    public void Commit_WhenNoHeldReservations_ReportsNothingHeld()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 5, TotalReserved = 0 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 5, Reserved = 0 };

        var result = item.Commit(
            Guid.NewGuid(), [], new Dictionary<int, StockLevel> { [7] = level }, DateTime.UtcNow);

        Assert.Equal(CommitOutcome.NothingHeld, result.Outcome);
        Assert.Empty(result.Movements);
        Assert.Equal(5, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
    }

    [Fact]
    public void Release_HeldReservation_ReturnsReservedOnly()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 10, TotalReserved = 4 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 10, Reserved = 4 };
        var orderId = Guid.NewGuid();
        var reservation = new StockReservation
        {
            OrderId = orderId,
            ProductId = 1,
            WarehouseId = 7,
            Quantity = 4,
            Status = ReservationStatus.Held,
        };
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

        var result = item.Release(orderId, [reservation], new Dictionary<int, StockLevel> { [7] = level }, now);

        Assert.Equal(ReleaseOutcome.Released, result.Outcome);
        Assert.Equal(10, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(10, level.OnHand);
        Assert.Equal(0, level.Reserved);
        Assert.Equal(ReservationStatus.Released, reservation.Status);

        var movement = Assert.Single(result.Movements);
        Assert.Equal(MovementType.Release, movement.Type);
        Assert.Equal(1, movement.ProductId);
        Assert.Equal(7, movement.WarehouseId);
        Assert.Equal(4, movement.Quantity);
        Assert.Equal(orderId, movement.OrderId);
        Assert.Equal(now, movement.OccurredAt);
    }

    [Fact]
    public void Release_CommittedReservation_RestoresOnHand()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 6, TotalReserved = 0 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 6, Reserved = 0 };
        var orderId = Guid.NewGuid();
        var reservation = new StockReservation
        {
            OrderId = orderId,
            ProductId = 1,
            WarehouseId = 7,
            Quantity = 4,
            Status = ReservationStatus.Committed,
        };

        var result = item.Release(orderId, [reservation], new Dictionary<int, StockLevel> { [7] = level }, DateTime.UtcNow);

        Assert.Equal(ReleaseOutcome.Released, result.Outcome);
        Assert.Equal(10, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(10, level.OnHand);
        Assert.Equal(0, level.Reserved);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Single(result.Movements);
    }

    [Fact]
    public void Release_MixedHeldAndCommitted_AppliesEachRule()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 12, TotalReserved = 3 };
        var heldLevel = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 7, Reserved = 3 };
        var committedLevel = new StockLevel { ProductId = 1, WarehouseId = 8, OnHand = 5, Reserved = 0 };
        var orderId = Guid.NewGuid();
        var held = new StockReservation
        {
            OrderId = orderId,
            ProductId = 1,
            WarehouseId = 7,
            Quantity = 3,
            Status = ReservationStatus.Held,
        };
        var committed = new StockReservation
        {
            OrderId = orderId,
            ProductId = 1,
            WarehouseId = 8,
            Quantity = 4,
            Status = ReservationStatus.Committed,
        };
        var levels = new Dictionary<int, StockLevel> { [7] = heldLevel, [8] = committedLevel };

        var result = item.Release(orderId, [held, committed], levels, DateTime.UtcNow);

        Assert.Equal(ReleaseOutcome.Released, result.Outcome);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(16, item.TotalOnHand);
        Assert.Equal(0, heldLevel.Reserved);
        Assert.Equal(7, heldLevel.OnHand);
        Assert.Equal(9, committedLevel.OnHand);
        Assert.Equal(ReservationStatus.Released, held.Status);
        Assert.Equal(ReservationStatus.Released, committed.Status);
        Assert.Equal(2, result.Movements.Count);
    }

    [Fact]
    public void Release_WhenAlreadyReleased_IsIdempotentNoOp()
    {
        var item = new StockItem { ProductId = 1, TotalOnHand = 5, TotalReserved = 0 };
        var level = new StockLevel { ProductId = 1, WarehouseId = 7, OnHand = 5, Reserved = 0 };
        var orderId = Guid.NewGuid();
        var reservation = new StockReservation
        {
            OrderId = orderId,
            ProductId = 1,
            WarehouseId = 7,
            Quantity = 2,
            Status = ReservationStatus.Released,
        };

        var result = item.Release(orderId, [reservation], new Dictionary<int, StockLevel> { [7] = level }, DateTime.UtcNow);

        Assert.Equal(ReleaseOutcome.NothingToRelease, result.Outcome);
        Assert.Empty(result.Movements);
        Assert.Equal(5, item.TotalOnHand);
        Assert.Equal(0, item.TotalReserved);
        Assert.Equal(5, level.OnHand);
        Assert.Equal(0, level.Reserved);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
    }
}
