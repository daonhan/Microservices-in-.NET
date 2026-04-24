using System.Net;
using System.Net.Http.Json;
using Shipping.Service.ApiModels;
using Shipping.Service.Models;

namespace Shipping.Tests.Api;

public class GetShipmentsByOrderTests : IntegrationTestBase
{
    public GetShipmentsByOrderTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Get_WhenUnauthenticated_ThenReturnsUnauthorized()
    {
        var response = await HttpClient.GetAsync($"/by-order/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenNoShipmentsForOrder_ThenReturnsNotFound()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/by-order/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenShipmentsExist_ThenReturnsThem()
    {
        var orderId = Guid.NewGuid();
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            CustomerId = "cust-1",
            WarehouseId = 1,
            Status = ShipmentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        shipment.AddLine(productId: 10, quantity: 2);
        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/by-order/{orderId}");

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<List<ShipmentResponse>>();
        Assert.NotNull(body);
        var single = Assert.Single(body);
        Assert.Equal(orderId, single.OrderId);
        Assert.Equal("cust-1", single.CustomerId);
        Assert.Equal(1, single.WarehouseId);
        Assert.Equal("Pending", single.Status);
        Assert.Single(single.Lines);
        Assert.Equal(10, single.Lines[0].ProductId);
        Assert.Equal(2, single.Lines[0].Quantity);
    }
}
