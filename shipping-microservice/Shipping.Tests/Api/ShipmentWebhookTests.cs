using System.Net;
using System.Net.Http.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.Carriers;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;

namespace Shipping.Tests.Api;

public class ShipmentWebhookTests : IntegrationTestBase
{
    private const string GroundSecret = "test-ground-secret";

    public ShipmentWebhookTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Webhook_WithoutSecret_ReturnsUnauthorized()
    {
        var (_, tracking) = await SeedShippedShipmentAsync();

        var response = await Factory.CreateClient().PostAsJsonAsync(
            $"/webhooks/carrier/{FakeGroundCarrierGateway.Key}",
            new { trackingNumber = tracking, statusCode = (int)CarrierStatusCode.InTransit });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_WithWrongSecret_ReturnsUnauthorized()
    {
        var (_, tracking) = await SeedShippedShipmentAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Carrier-Secret", "wrong");
        var response = await client.PostAsJsonAsync(
            $"/webhooks/carrier/{FakeGroundCarrierGateway.Key}",
            new { trackingNumber = tracking, statusCode = (int)CarrierStatusCode.InTransit });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_ForUnknownCarrier_ReturnsUnauthorized()
    {
        // No shared secret configured for "some-other-carrier", so secret check rejects first.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Carrier-Secret", "anything");
        var response = await client.PostAsJsonAsync(
            "/webhooks/carrier/some-other-carrier",
            new { trackingNumber = "X", statusCode = 2 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_ForUnknownTracking_ReturnsNotFound()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Carrier-Secret", GroundSecret);

        var response = await client.PostAsJsonAsync(
            $"/webhooks/carrier/{FakeGroundCarrierGateway.Key}",
            new { trackingNumber = "GND-DOESNOTEXIST", statusCode = (int)CarrierStatusCode.InTransit });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_WithInTransitStatus_AdvancesShipmentAndTagsSource()
    {
        var (shipmentId, tracking) = await SeedShippedShipmentAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Carrier-Secret", GroundSecret);
        var response = await client.PostAsJsonAsync(
            $"/webhooks/carrier/{FakeGroundCarrierGateway.Key}",
            new { trackingNumber = tracking, statusCode = (int)CarrierStatusCode.InTransit });

        response.EnsureSuccessStatusCode();

        var shipment = await ShippingContext.Shipments
            .AsNoTracking()
            .Include(s => s.StatusHistory)
            .FirstAsync(s => s.Id == shipmentId);

        Assert.Equal(ShipmentStatus.InTransit, shipment.Status);
        Assert.Contains(
            shipment.StatusHistory,
            h => h.Status == ShipmentStatus.InTransit && h.Source == ShipmentStatusSource.CarrierWebhook);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var events = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)
            && e.Data.Contains(shipmentId.ToString(), StringComparison.OrdinalIgnoreCase)
            && e.Data.Contains($"\"ToStatus\":{(int)ShipmentStatus.InTransit}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Webhook_OnTerminalShipment_DoesNotRegressState()
    {
        // Seed a Delivered shipment (terminal), then deliver a contradicting webhook.
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
            labelRef: $"label://{FakeGroundCarrierGateway.Key}/{tracking}",
            quotedPrice: Money.Usd(5m),
            shippingAddress: new ShippingAddress("A", "B", null, "C", null, "00000", "US"),
            occurredAt: now,
            source: ShipmentStatusSource.Admin));
        Assert.True(shipment.TryDeliver(now, ShipmentStatusSource.Admin));

        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Carrier-Secret", GroundSecret);
        var response = await client.PostAsJsonAsync(
            $"/webhooks/carrier/{FakeGroundCarrierGateway.Key}",
            new { trackingNumber = tracking, statusCode = (int)CarrierStatusCode.Failed });

        response.EnsureSuccessStatusCode();

        var after = await ShippingContext.Shipments
            .AsNoTracking()
            .FirstAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.Delivered, after.Status);
    }

    private async Task<(Guid ShipmentId, string TrackingNumber)> SeedShippedShipmentAsync()
    {
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
            labelRef: $"label://{FakeGroundCarrierGateway.Key}/{tracking}",
            quotedPrice: Money.Usd(5m),
            shippingAddress: new ShippingAddress("A", "B", null, "C", null, "00000", "US"),
            occurredAt: now,
            source: ShipmentStatusSource.Admin));

        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();

        return (shipmentId, tracking);
    }
}
