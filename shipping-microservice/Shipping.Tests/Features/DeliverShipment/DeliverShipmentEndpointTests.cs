using System.Net;
using System.Net.Http.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Tests.Authentication;

namespace Shipping.Tests.Features.DeliverShipment;

public class DeliverShipmentEndpointTests : IntegrationTestBase
{
    public DeliverShipmentEndpointTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Deliver_WhenShipmentShipped_EmitsDeliveredAndStatusChanged()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Shipped);

        var response = await CreateAuthenticatedClient().PostAsync($"/{shipmentId}/deliver", content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("Delivered", body.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var events = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentDeliveredEvent), StringComparison.Ordinal));
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)
            && e.Data.Contains($"\"ToStatus\":{(int)ShipmentStatus.Delivered}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deliver_WhenShipmentPending_ReturnsConflict()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Pending);

        var response = await CreateAuthenticatedClient().PostAsync($"/{shipmentId}/deliver", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deliver_WhenNonAdmin_ReturnsForbidden()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Shipped);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");
        var response = await client.PostAsync($"/{shipmentId}/deliver", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
