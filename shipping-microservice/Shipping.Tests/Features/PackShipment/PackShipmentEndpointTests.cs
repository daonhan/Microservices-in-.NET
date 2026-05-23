using System.Net;
using System.Net.Http.Json;
using Shipping.Service.Domain;
using Shipping.Service.Features.GetShipmentsByOrder;

namespace Shipping.Tests.Features.PackShipment;

public class PackShipmentEndpointTests : IntegrationTestBase
{
    public PackShipmentEndpointTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
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
