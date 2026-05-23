using System.Net;
using System.Net.Http.Json;
using Shipping.Service.Domain;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Tests.Authentication;

namespace Shipping.Tests.Features.GetShipmentsByOrder;

public class ShipmentOwnershipByOrderTests : IntegrationTestBase
{
    public ShipmentOwnershipByOrderTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetByOrder_WhenCustomerOwnsShipment_ReturnsOk()
    {
        var (orderId, _, customerId) = await SeedShipmentAsync();

        var response = await CreateCustomerClient(customerId).GetAsync($"/by-order/{orderId}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<List<ShipmentResponse>>();
        Assert.NotNull(body);
        Assert.Single(body);
    }

    [Fact]
    public async Task GetByOrder_WhenCustomerIsNotOwner_ReturnsForbidden()
    {
        var (orderId, _, _) = await SeedShipmentAsync();

        var response = await CreateCustomerClient("different-customer").GetAsync($"/by-order/{orderId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetByOrder_WhenAdmin_ReturnsOkRegardlessOfOwnership()
    {
        var (orderId, _, _) = await SeedShipmentAsync();

        var response = await CreateAuthenticatedClient().GetAsync($"/by-order/{orderId}");

        response.EnsureSuccessStatusCode();
    }

    private async Task<(Guid OrderId, Guid ShipmentId, string CustomerId)> SeedShipmentAsync()
    {
        var orderId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";
        var shipment = Shipment.Create(
            id: Guid.NewGuid(),
            orderId: orderId,
            customerId: customerId,
            warehouseId: 1,
            createdAt: DateTime.UtcNow);
        shipment.AddLine(productId: 10, quantity: 2);
        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();
        return (orderId, shipment.Id, customerId);
    }

    private HttpClient CreateCustomerClient(string customerId)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");
        client.DefaultRequestHeaders.Add(TestAuthHandler.CustomerIdHeader, customerId);
        return client;
    }
}
