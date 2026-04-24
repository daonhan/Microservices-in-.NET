using System.Net;
using System.Net.Http.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.ApiModels;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;
using Shipping.Tests.Authentication;

namespace Shipping.Tests.Api;

public class ShipmentTransitionEndpointsTests : IntegrationTestBase
{
    public ShipmentTransitionEndpointsTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Pick_WhenAdmin_TransitionsAndEmitsStatusChanged()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Pending);

        var response = await CreateAuthenticatedClient().PostAsync($"/{shipmentId}/pick", content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("Picked", body.Status);

        await AssertStatusChangedInOutbox(shipmentId, ShipmentStatus.Picked);
    }

    [Fact]
    public async Task Pack_WhenShipmentInPicked_Succeeds()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Picked);

        var response = await CreateAuthenticatedClient().PostAsync($"/{shipmentId}/pack", content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("Packed", body.Status);
    }

    [Fact]
    public async Task Pack_WhenShipmentInPending_ReturnsConflict()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Pending);

        var response = await CreateAuthenticatedClient().PostAsync($"/{shipmentId}/pack", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Pick_WhenNonAdmin_ReturnsForbidden()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Pending);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");
        var response = await client.PostAsync($"/{shipmentId}/pick", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pick_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Pending);

        var response = await HttpClient.PostAsync($"/{shipmentId}/pick", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    [Fact]
    public async Task Transition_WhenShipmentNotFound_ReturnsNotFound()
    {
        var response = await CreateAuthenticatedClient().PostAsync($"/{Guid.NewGuid()}/pick", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private async Task AssertStatusChangedInOutbox(Guid shipmentId, ShipmentStatus expected)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var events = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)
            && e.Data.Contains(shipmentId.ToString(), StringComparison.OrdinalIgnoreCase)
            && e.Data.Contains($"\"ToStatus\":{(int)expected}", StringComparison.Ordinal));
    }
}
