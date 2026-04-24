using System.Net;
using System.Net.Http.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.ApiModels;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;
using Shipping.Tests.Authentication;

namespace Shipping.Tests.Api;

public class ShipmentTerminalTransitionTests : IntegrationTestBase
{
    public ShipmentTerminalTransitionTests(ShippingWebApplicationFactory webApplicationFactory)
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

    [Fact]
    public async Task Fail_WhenShipmentShipped_EmitsFailedWithReasonAndStatusChanged()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Shipped);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{shipmentId}/fail",
            new FailShipmentRequest(Reason: "Lost in transit"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("Failed", body.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var events = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentFailedEvent), StringComparison.Ordinal)
            && e.Data.Contains("Lost in transit", StringComparison.Ordinal));
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)
            && e.Data.Contains($"\"ToStatus\":{(int)ShipmentStatus.Failed}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fail_WithoutReason_ReturnsBadRequest()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Shipped);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{shipmentId}/fail",
            new FailShipmentRequest(Reason: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fail_WhenShipmentPacked_ReturnsConflict()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Packed);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{shipmentId}/fail",
            new FailShipmentRequest(Reason: "Too early"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Return_WhenShipmentShipped_EmitsReturnedWithReasonAndStatusChanged()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Shipped);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{shipmentId}/return",
            new ReturnShipmentRequest(Reason: "Customer refused delivery"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("Returned", body.Status);

        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var events = await outboxStore.GetUnpublishedOutboxEvents();
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentReturnedEvent), StringComparison.Ordinal)
            && e.Data.Contains("Customer refused delivery", StringComparison.Ordinal));
        Assert.Contains(events, e =>
            e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)
            && e.Data.Contains($"\"ToStatus\":{(int)ShipmentStatus.Returned}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Return_WithoutReason_ReturnsBadRequest()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Shipped);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{shipmentId}/return",
            new ReturnShipmentRequest(Reason: "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Return_WhenNonAdmin_ReturnsForbidden()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Shipped);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");
        var response = await client.PostAsJsonAsync(
            $"/{shipmentId}/return",
            new ReturnShipmentRequest(Reason: "test"));

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
