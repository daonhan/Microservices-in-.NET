using Shipping.Service.Models;

namespace Shipping.Tests.Domain;

public class ShipmentTests
{
    private static readonly DateTime CreatedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Shipment NewShipment()
    {
        var shipment = Shipment.Create(
            id: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            customerId: "cust-1",
            warehouseId: 1,
            createdAt: CreatedAt);
        shipment.AddLine(productId: 10, quantity: 1);
        return shipment;
    }

    [Fact]
    public void Create_SetsPendingStatus_AndAppendsInitialHistory()
    {
        var shipment = NewShipment();

        Assert.Equal(ShipmentStatus.Pending, shipment.Status);
        var entry = Assert.Single(shipment.StatusHistory);
        Assert.Equal(ShipmentStatus.Pending, entry.Status);
        Assert.Equal(CreatedAt, entry.OccurredAt);
        Assert.Equal(ShipmentStatusSource.System, entry.Source);
        Assert.Null(entry.Reason);
    }

    [Fact]
    public void TryPick_FromPending_Succeeds_AndAppendsHistory()
    {
        var shipment = NewShipment();
        var at = CreatedAt.AddMinutes(1);

        var ok = shipment.TryPick(at, ShipmentStatusSource.Admin);

        Assert.True(ok);
        Assert.Equal(ShipmentStatus.Picked, shipment.Status);
        Assert.Equal(2, shipment.StatusHistory.Count);
        var last = shipment.StatusHistory[^1];
        Assert.Equal(ShipmentStatus.Picked, last.Status);
        Assert.Equal(at, last.OccurredAt);
        Assert.Equal(ShipmentStatusSource.Admin, last.Source);
    }

    [Fact]
    public void HappyPath_PickPackShipDeliver_AllTransitionsLegal()
    {
        var shipment = NewShipment();
        var at = CreatedAt;

        Assert.True(shipment.TryPick(at = at.AddMinutes(1), ShipmentStatusSource.Admin));
        Assert.True(shipment.TryPack(at = at.AddMinutes(1), ShipmentStatusSource.Admin));
        Assert.True(shipment.TryDispatch(at = at.AddMinutes(1), ShipmentStatusSource.Admin));
        Assert.True(shipment.TryMarkInTransit(at = at.AddMinutes(1), ShipmentStatusSource.CarrierPoll));
        Assert.True(shipment.TryDeliver(at.AddMinutes(1), ShipmentStatusSource.CarrierWebhook));

        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
        Assert.Equal(6, shipment.StatusHistory.Count);
        Assert.Equal(
            new[]
            {
                ShipmentStatus.Pending,
                ShipmentStatus.Picked,
                ShipmentStatus.Packed,
                ShipmentStatus.Shipped,
                ShipmentStatus.InTransit,
                ShipmentStatus.Delivered,
            },
            shipment.StatusHistory.Select(h => h.Status).ToArray());
    }

    [Fact]
    public void TryDeliver_DirectlyFromShipped_Succeeds_ForManualOverride()
    {
        var shipment = NewShipment();
        shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin);

        var ok = shipment.TryDeliver(CreatedAt.AddHours(1), ShipmentStatusSource.Admin);

        Assert.True(ok);
        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
    }

    [Fact]
    public void TryPack_FromPending_Fails()
    {
        var shipment = NewShipment();

        var ok = shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);

        Assert.False(ok);
        Assert.Equal(ShipmentStatus.Pending, shipment.Status);
        Assert.Single(shipment.StatusHistory);
    }

    [Fact]
    public void TryDispatch_FromPending_Fails()
    {
        var shipment = NewShipment();

        Assert.False(shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin));
        Assert.Equal(ShipmentStatus.Pending, shipment.Status);
    }

    [Fact]
    public void TryDeliver_FromPending_Fails()
    {
        var shipment = NewShipment();

        Assert.False(shipment.TryDeliver(CreatedAt, ShipmentStatusSource.Admin));
    }

    [Fact]
    public void TryMarkInTransit_FromPacked_Fails()
    {
        var shipment = NewShipment();
        shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);

        Assert.False(shipment.TryMarkInTransit(CreatedAt, ShipmentStatusSource.CarrierPoll));
        Assert.Equal(ShipmentStatus.Packed, shipment.Status);
    }

    [Fact]
    public void TryCancel_FromPending_Picked_Packed_Succeeds()
    {
        foreach (var advance in new Action<Shipment>[]
        {
            _ => { },
            s => s.TryPick(CreatedAt, ShipmentStatusSource.Admin),
            s =>
            {
                s.TryPick(CreatedAt, ShipmentStatusSource.Admin);
                s.TryPack(CreatedAt, ShipmentStatusSource.Admin);
            },
        })
        {
            var shipment = NewShipment();
            advance(shipment);

            var ok = shipment.TryCancel(CreatedAt.AddHours(1), ShipmentStatusSource.Admin, reason: "customer refund");

            Assert.True(ok);
            Assert.Equal(ShipmentStatus.Cancelled, shipment.Status);
            Assert.Equal("customer refund", shipment.StatusHistory[^1].Reason);
        }
    }

    [Fact]
    public void TryCancel_AfterShipped_Fails()
    {
        var shipment = NewShipment();
        shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin);

        Assert.False(shipment.TryCancel(CreatedAt.AddHours(1), ShipmentStatusSource.Admin));
        Assert.Equal(ShipmentStatus.Shipped, shipment.Status);
    }

    [Fact]
    public void TryFail_FromShipped_SucceedsWithReason()
    {
        var shipment = NewShipment();
        shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin);

        var ok = shipment.TryFail("lost in transit", CreatedAt.AddDays(1), ShipmentStatusSource.CarrierWebhook);

        Assert.True(ok);
        Assert.Equal(ShipmentStatus.Failed, shipment.Status);
        Assert.Equal("lost in transit", shipment.StatusHistory[^1].Reason);
    }

    [Fact]
    public void TryReturn_FromInTransit_SucceedsWithReason()
    {
        var shipment = NewShipment();
        shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin);
        shipment.TryMarkInTransit(CreatedAt, ShipmentStatusSource.CarrierPoll);

        var ok = shipment.TryReturn("refused by customer", CreatedAt.AddDays(1), ShipmentStatusSource.Admin);

        Assert.True(ok);
        Assert.Equal(ShipmentStatus.Returned, shipment.Status);
        Assert.Equal("refused by customer", shipment.StatusHistory[^1].Reason);
    }

    [Fact]
    public void TryFail_BeforeShipped_Fails()
    {
        var shipment = NewShipment();
        shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);

        Assert.False(shipment.TryFail("nope", CreatedAt, ShipmentStatusSource.Admin));
        Assert.Equal(ShipmentStatus.Picked, shipment.Status);
    }

    [Theory]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.Failed)]
    [InlineData(ShipmentStatus.Returned)]
    public void TerminalStates_RejectAllFurtherTransitions(ShipmentStatus terminal)
    {
        var shipment = MoveToTerminal(terminal);
        var historyCountBefore = shipment.StatusHistory.Count;

        Assert.False(shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin));
        Assert.False(shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin));
        Assert.False(shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin));
        Assert.False(shipment.TryMarkInTransit(CreatedAt, ShipmentStatusSource.CarrierPoll));
        Assert.False(shipment.TryDeliver(CreatedAt, ShipmentStatusSource.Admin));
        Assert.False(shipment.TryFail("x", CreatedAt, ShipmentStatusSource.Admin));
        Assert.False(shipment.TryReturn("x", CreatedAt, ShipmentStatusSource.Admin));
        Assert.False(shipment.TryCancel(CreatedAt, ShipmentStatusSource.Admin));

        Assert.Equal(terminal, shipment.Status);
        Assert.Equal(historyCountBefore, shipment.StatusHistory.Count);
    }

    private static Shipment MoveToTerminal(ShipmentStatus terminal)
    {
        var shipment = NewShipment();
        switch (terminal)
        {
            case ShipmentStatus.Cancelled:
                shipment.TryCancel(CreatedAt, ShipmentStatusSource.Admin);
                break;
            case ShipmentStatus.Delivered:
                shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryMarkInTransit(CreatedAt, ShipmentStatusSource.CarrierPoll);
                shipment.TryDeliver(CreatedAt, ShipmentStatusSource.CarrierWebhook);
                break;
            case ShipmentStatus.Failed:
                shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryFail("boom", CreatedAt, ShipmentStatusSource.CarrierWebhook);
                break;
            case ShipmentStatus.Returned:
                shipment.TryPick(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryPack(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryDispatch(CreatedAt, ShipmentStatusSource.Admin);
                shipment.TryReturn("rma", CreatedAt, ShipmentStatusSource.Admin);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(terminal));
        }
        return shipment;
    }
}
