using Inventory.Service.Models;

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
}
