using System.Net;
using System.Net.Http.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Features.CancelShipment;
using Shipping.Service.Features.GetShipmentsByOrder;

namespace Shipping.Tests.Features.CancelShipment;

public class CancelShipmentEndpointTests : IntegrationTestBase
{
    public CancelShipmentEndpointTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Cancel_WhenShipmentInPacked_EmitsCancelledAndStatusChanged()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Packed);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{shipmentId}/cancel",
            new CancelShipmentRequest(Reason: "Customer request"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("Cancelled", body.Status);

        using var outboxScope = Factory.Services.CreateScope();
        var outboxStore = outboxScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(outboxEvents, e => e.EventType.Contains(nameof(ShipmentCancelledEvent), StringComparison.Ordinal));
        Assert.Contains(outboxEvents, e => e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancel_WhenShipmentIsShipped_ReturnsConflict()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Shipped);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{shipmentId}/cancel",
            new CancelShipmentRequest(Reason: null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<Guid> SeedShipmentAsync(ShipmentStatus targetStatus)
    {
        var shipment = Shipment.Create(
            id: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            customerId: $"cust-{Guid.NewGuid():N}",
            warehouseId: 1,
            createdAt: DateTime.UtcNow);
        shipment.AddLine(productId: 1, quantity: 1);

        var now = DateTime.UtcNow;
        if (targetStatus is ShipmentStatus.Picked or ShipmentStatus.Packed or ShipmentStatus.Shipped)
        {
            Assert.True(shipment.TryPick(now, ShipmentStatusSource.Admin));
        }

        if (targetStatus is ShipmentStatus.Packed or ShipmentStatus.Shipped)
        {
            Assert.True(shipment.TryPack(now, ShipmentStatusSource.Admin));
        }

        if (targetStatus == ShipmentStatus.Shipped)
        {
            Assert.True(shipment.TryDispatch(now, ShipmentStatusSource.Admin));
        }

        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();
        return shipment.Id;
    }
}
