using System.Net;
using System.Net.Http.Json;
using Shipping.Service.Domain;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Tests.Authentication;

namespace Shipping.Tests.Features.GetShipmentById;

public class ShipmentOwnershipByIdTests : IntegrationTestBase
{
    public ShipmentOwnershipByIdTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetById_WhenCustomerOwnsShipment_ReturnsOk()
    {
        var (_, shipmentId, customerId) = await SeedShipmentAsync();

        var response = await CreateCustomerClient(customerId).GetAsync($"/{shipmentId}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal(shipmentId, body.ShipmentId);
    }

    [Fact]
    public async Task GetById_WhenCustomerIsNotOwner_ReturnsForbidden()
    {
        var (_, shipmentId, _) = await SeedShipmentAsync();

        var response = await CreateCustomerClient("different-customer").GetAsync($"/{shipmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenAdmin_ReturnsOkRegardlessOfOwnership()
    {
        var (_, shipmentId, _) = await SeedShipmentAsync();

        var response = await CreateAuthenticatedClient().GetAsync($"/{shipmentId}");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetById_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClient.GetAsync($"/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenNotFound_ReturnsNotFound()
    {
        var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
