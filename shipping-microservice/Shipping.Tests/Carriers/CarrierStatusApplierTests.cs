using Shipping.Service.Carriers;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;

namespace Shipping.Tests.Carriers;

public class CarrierStatusApplierTests
{
    [Fact]
    public async Task Given_DeliveredCarrierStatus_When_Applied_Then_ReturnsMilestoneAndStatusChangedEvents()
    {
        var shipmentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 16, 9, 30, 0, DateTimeKind.Utc);
        var shipment = CreateShippedShipment(shipmentId, orderId);

        var events = await CarrierStatusApplier.ApplyAsync(
            shipment,
            new CarrierStatus(CarrierStatusCode.Delivered, Detail: null),
            ShipmentStatusSource.CarrierWebhook,
            occurredAt);

        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
        Assert.Collection(
            events,
            evt =>
            {
                var delivered = Assert.IsType<ShipmentDeliveredEvent>(evt);
                Assert.Equal(shipmentId, delivered.ShipmentId);
                Assert.Equal(orderId, delivered.OrderId);
                Assert.Equal(occurredAt, delivered.OccurredAt);
            },
            evt =>
            {
                var changed = Assert.IsType<ShipmentStatusChangedEvent>(evt);
                Assert.Equal(shipmentId, changed.ShipmentId);
                Assert.Equal(ShipmentStatus.Shipped, changed.FromStatus);
                Assert.Equal(ShipmentStatus.Delivered, changed.ToStatus);
                Assert.Equal(occurredAt, changed.OccurredAt);
            });
    }

    [Fact]
    public async Task Given_FailedCarrierStatus_When_Applied_Then_ReturnsFailureAndStatusChangedEvents()
    {
        var shipmentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 16, 9, 45, 0, DateTimeKind.Utc);
        var shipment = CreateShippedShipment(shipmentId, orderId);

        var events = await CarrierStatusApplier.ApplyAsync(
            shipment,
            new CarrierStatus(CarrierStatusCode.Failed, Detail: "Carrier damaged package"),
            ShipmentStatusSource.CarrierPoll,
            occurredAt);

        Assert.Equal(ShipmentStatus.Failed, shipment.Status);
        Assert.Collection(
            events,
            evt =>
            {
                var failed = Assert.IsType<ShipmentFailedEvent>(evt);
                Assert.Equal(shipmentId, failed.ShipmentId);
                Assert.Equal(orderId, failed.OrderId);
                Assert.Equal("Carrier damaged package", failed.Reason);
                Assert.Equal(occurredAt, failed.OccurredAt);
            },
            evt =>
            {
                var changed = Assert.IsType<ShipmentStatusChangedEvent>(evt);
                Assert.Equal(shipmentId, changed.ShipmentId);
                Assert.Equal(ShipmentStatus.Shipped, changed.FromStatus);
                Assert.Equal(ShipmentStatus.Failed, changed.ToStatus);
                Assert.Equal(occurredAt, changed.OccurredAt);
            });
    }

    private static Shipment CreateShippedShipment(Guid shipmentId, Guid orderId)
    {
        var shipment = Shipment.Create(
            id: shipmentId,
            orderId: orderId,
            customerId: "cust-carrier-status",
            warehouseId: 1,
            createdAt: new DateTime(2026, 5, 16, 8, 0, 0, DateTimeKind.Utc));
        shipment.AddLine(productId: 1, quantity: 1);

        var transitionAt = new DateTime(2026, 5, 16, 8, 5, 0, DateTimeKind.Utc);
        Assert.True(shipment.TryPick(transitionAt, ShipmentStatusSource.Admin));
        Assert.True(shipment.TryPack(transitionAt, ShipmentStatusSource.Admin));
        Assert.True(shipment.TryDispatch(
            carrierKey: FakeGroundCarrierGateway.Key,
            trackingNumber: $"GND-{shipmentId:N}".ToUpperInvariant(),
            labelRef: "label://fake-ground/test",
            quotedPrice: Money.Usd(5m),
            shippingAddress: new ShippingAddress("A", "B", null, "C", null, "00000", "US"),
            occurredAt: transitionAt,
            source: ShipmentStatusSource.Admin));

        return shipment;
    }
}
