using System.Net;
using System.Net.Http.Json;
using Shipping.Service.ApiModels;
using Shipping.Service.Models;
using Shipping.Tests.Authentication;

namespace Shipping.Tests.Api;

public class ListShipmentsTests : IntegrationTestBase
{
    public ListShipmentsTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task List_WhenNonAdmin_ReturnsForbidden()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClient.GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_FilterByStatus_ReturnsOnlyMatching()
    {
        var pending = await SeedShipment(1, ShipmentStatus.Pending);
        var picked = await SeedShipment(1, ShipmentStatus.Picked);

        var response = await CreateAuthenticatedClient().GetAsync("/?status=Picked");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<List<ShipmentResponse>>();
        Assert.NotNull(body);
        Assert.Contains(body, s => s.ShipmentId == picked);
        Assert.DoesNotContain(body, s => s.ShipmentId == pending);
    }

    [Fact]
    public async Task List_FilterByWarehouse_ReturnsOnlyMatching()
    {
        var east = await SeedShipment(1, ShipmentStatus.Pending);
        var west = await SeedShipment(2, ShipmentStatus.Pending);

        var response = await CreateAuthenticatedClient().GetAsync("/?warehouseId=2");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<List<ShipmentResponse>>();
        Assert.NotNull(body);
        Assert.Contains(body, s => s.ShipmentId == west);
        Assert.DoesNotContain(body, s => s.ShipmentId == east);
    }

    [Fact]
    public async Task List_WithUnknownStatus_ReturnsBadRequest()
    {
        var response = await CreateAuthenticatedClient().GetAsync("/?status=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> SeedShipment(int warehouseId, ShipmentStatus targetStatus)
    {
        var shipment = Shipment.Create(
            id: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            customerId: $"cust-{Guid.NewGuid():N}",
            warehouseId: warehouseId,
            createdAt: DateTime.UtcNow);
        shipment.AddLine(1, 1);

        var now = DateTime.UtcNow;
        if (targetStatus == ShipmentStatus.Picked)
        {
            Assert.True(shipment.TryPick(now, ShipmentStatusSource.Admin));
        }

        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();
        return shipment.Id;
    }
}
