using System.Net;
using System.Net.Http.Json;
using Shipping.Service.Domain;
using Shipping.Service.Features.GetCarrierQuotes;
using Shipping.Tests.Authentication;

namespace Shipping.Tests.Features.GetCarrierQuotes;

public class GetCarrierQuotesEndpointTests : IntegrationTestBase
{
    public GetCarrierQuotesEndpointTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetQuotes_WhenAdmin_ReturnsRankedQuotes()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Packed);

        var response = await CreateAuthenticatedClient().GetAsync($"/{shipmentId}/quotes");

        response.EnsureSuccessStatusCode();
        var quotes = await response.Content.ReadFromJsonAsync<List<CarrierQuoteResponse>>();
        Assert.NotNull(quotes);
        Assert.Equal(2, quotes.Count);
        // Ranked by cheapest first.
        Assert.True(quotes[0].PriceAmount <= quotes[1].PriceAmount);
    }

    [Fact]
    public async Task GetQuotes_WhenNonAdmin_ReturnsForbidden()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Packed);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");
        var response = await client.GetAsync($"/{shipmentId}/quotes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetQuotes_WhenShipmentMissing_ReturnsNotFound()
    {
        var response = await CreateAuthenticatedClient().GetAsync($"/{Guid.NewGuid()}/quotes");

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
        shipment.AddLine(productId: 1, quantity: 2);

        var now = DateTime.UtcNow;
        if (targetStatus is ShipmentStatus.Picked or ShipmentStatus.Packed)
        {
            Assert.True(shipment.TryPick(now, ShipmentStatusSource.Admin));
        }

        if (targetStatus is ShipmentStatus.Packed)
        {
            Assert.True(shipment.TryPack(now, ShipmentStatusSource.Admin));
        }

        ShippingContext.Shipments.Add(shipment);
        await ShippingContext.SaveChangesAsync();
        return shipment.Id;
    }
}
