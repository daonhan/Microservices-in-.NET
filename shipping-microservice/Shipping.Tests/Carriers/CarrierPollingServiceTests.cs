using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.Carriers;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;

namespace Shipping.Tests.Carriers;

public class CarrierPollingServiceTests : IntegrationTestBase
{
    public CarrierPollingServiceTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task PollOnce_WhenCarrierReportsDelivered_AdvancesShipmentAndEmitsEvents()
    {
        var (shipmentId, trackingNumber) = await SeedShippedShipmentAsync(
            FakeGroundCarrierGateway.Key,
            dispatchedMinutesAgo: 90);

        var pollingService = Factory.Services.GetRequiredService<CarrierPollingService>();
        var updated = await pollingService.PollOnceAsync();

        Assert.True(updated >= 1);

        var shipment = await ShippingContext.Shipments
            .AsNoTracking()
            .FirstAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var events = await outboxStore.GetUnpublishedOutboxEvents();

        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentDeliveredEvent), StringComparison.Ordinal)
            && e.Data.Contains(shipmentId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)
            && e.Data.Contains(shipmentId.ToString(), StringComparison.OrdinalIgnoreCase)
            && e.Data.Contains($"\"ToStatus\":{(int)ShipmentStatus.Delivered}", StringComparison.Ordinal));

        // Status history is tagged with CarrierPoll source.
        var shipmentWithHistory = await ShippingContext.Shipments
            .AsNoTracking()
            .Include(s => s.StatusHistory)
            .FirstAsync(s => s.Id == shipmentId);
        Assert.Contains(
            shipmentWithHistory.StatusHistory,
            h => h.Status == ShipmentStatus.Delivered && h.Source == ShipmentStatusSource.CarrierPoll);
    }

    [Fact]
    public async Task PollOnce_WhenCarrierReportsInTransit_TransitionsShippedToInTransit()
    {
        var (shipmentId, _) = await SeedShippedShipmentAsync(
            FakeGroundCarrierGateway.Key,
            dispatchedMinutesAgo: 20);

        var pollingService = Factory.Services.GetRequiredService<CarrierPollingService>();
        await pollingService.PollOnceAsync();

        var shipment = await ShippingContext.Shipments
            .AsNoTracking()
            .FirstAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.InTransit, shipment.Status);
    }

    [Fact]
    public async Task PollOnce_WhenShipmentAlreadyTerminal_DoesNotRegress()
    {
        // Seed a Delivered shipment even though the carrier would now say "Unknown".
        var shipmentId = Guid.NewGuid();
        var tracking = $"GND-{shipmentId:N}".ToUpperInvariant();

        var shipment = Shipment.Create(
            id: shipmentId,
            orderId: Guid.NewGuid(),
            customerId: $"cust-{Guid.NewGuid():N}",
            warehouseId: 1,
            createdAt: DateTime.UtcNow);
        shipment.AddLine(1, 1);
        var now = DateTime.UtcNow;
        Assert.True(shipment.TryPick(now, ShipmentStatusSource.Admin));
        Assert.True(shipment.TryPack(now, ShipmentStatusSource.Admin));
        Assert.True(shipment.TryDispatch(
            carrierKey: FakeGroundCarrierGateway.Key,
            trackingNumber: tracking,
            labelRef: "label://test",
            quotedPrice: Money.Usd(5m),
            shippingAddress: new ShippingAddress("A", "B", null, "C", null, "00000", "US"),
            occurredAt: now,
            source: ShipmentStatusSource.Admin));
        Assert.True(shipment.TryDeliver(now, ShipmentStatusSource.Admin));

        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();

        var pollingService = Factory.Services.GetRequiredService<CarrierPollingService>();
        var updated = await pollingService.PollOnceAsync();

        // Delivered is not in the active set (only Shipped/InTransit are polled), so nothing happens.
        Assert.Equal(0, updated);

        var after = await ShippingContext.Shipments
            .AsNoTracking()
            .FirstAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.Delivered, after.Status);
    }

    private async Task<(Guid ShipmentId, string TrackingNumber)> SeedShippedShipmentAsync(
        string carrierKey,
        int dispatchedMinutesAgo)
    {
        var shipmentId = Guid.NewGuid();
        var trackingPrefix = carrierKey == FakeGroundCarrierGateway.Key ? "GND" : "EXP";
        var tracking = $"{trackingPrefix}-{shipmentId:N}".ToUpperInvariant();

        var shipment = Shipment.Create(
            id: shipmentId,
            orderId: Guid.NewGuid(),
            customerId: $"cust-{Guid.NewGuid():N}",
            warehouseId: 1,
            createdAt: DateTime.UtcNow);
        shipment.AddLine(productId: 1, quantity: 1);

        var now = DateTime.UtcNow;
        Assert.True(shipment.TryPick(now, ShipmentStatusSource.Admin));
        Assert.True(shipment.TryPack(now, ShipmentStatusSource.Admin));
        Assert.True(shipment.TryDispatch(
            carrierKey: carrierKey,
            trackingNumber: tracking,
            labelRef: $"label://{carrierKey}/{tracking}",
            quotedPrice: Money.Usd(5m),
            shippingAddress: new ShippingAddress("A", "B", null, "C", null, "00000", "US"),
            occurredAt: now,
            source: ShipmentStatusSource.Admin));

        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();

        var registry = Factory.Services.GetRequiredService<FakeCarrierDispatchRegistry>();
        registry.Record(tracking, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(dispatchedMinutesAgo));

        return (shipmentId, tracking);
    }
}
