using System.Net;
using System.Net.Http.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.ApiModels;
using Shipping.Service.Carriers;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;
using Shipping.Tests.Authentication;

namespace Shipping.Tests.Api;

public class ShipmentDispatchEndpointsTests : IntegrationTestBase
{
    public ShipmentDispatchEndpointsTests(ShippingWebApplicationFactory webApplicationFactory)
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

    [Fact]
    public async Task Dispatch_WhenShipmentPacked_StoresCarrierAndEmitsEvents()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Packed);
        var request = NewDispatchRequest(FakeGroundCarrierGateway.Key);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync($"/{shipmentId}/dispatch", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DispatchShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("Shipped", body.Status);
        Assert.Equal(FakeGroundCarrierGateway.Key, body.CarrierKey);
        Assert.False(string.IsNullOrWhiteSpace(body.TrackingNumber));
        Assert.False(string.IsNullOrWhiteSpace(body.LabelRef));
        Assert.True(body.QuotedPriceAmount > 0);

        using var outboxScope = Factory.Services.CreateScope();
        var outboxStore = outboxScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        Assert.Contains(outboxEvents, e =>
            e.EventType.Contains(nameof(ShipmentDispatchedEvent), StringComparison.Ordinal)
            && e.Data.Contains(shipmentId.ToString(), StringComparison.OrdinalIgnoreCase));

        Assert.Contains(outboxEvents, e =>
            e.EventType.Contains(nameof(ShipmentStatusChangedEvent), StringComparison.Ordinal)
            && e.Data.Contains(shipmentId.ToString(), StringComparison.OrdinalIgnoreCase)
            && e.Data.Contains($"\"ToStatus\":{(int)ShipmentStatus.Shipped}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dispatch_WhenShipmentPending_ReturnsConflict()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Pending);
        var request = NewDispatchRequest(FakeGroundCarrierGateway.Key);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync($"/{shipmentId}/dispatch", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Dispatch_WithUnknownCarrier_ReturnsBadRequest()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Packed);
        var request = NewDispatchRequest("no-such-carrier");

        var response = await CreateAuthenticatedClient().PostAsJsonAsync($"/{shipmentId}/dispatch", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Dispatch_WhenNonAdmin_ReturnsForbidden()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Packed);
        var request = NewDispatchRequest(FakeGroundCarrierGateway.Key);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");
        var response = await client.PostAsJsonAsync($"/{shipmentId}/dispatch", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dispatch_WithOverrideQuote_UsesOverriddenPrice()
    {
        var shipmentId = await SeedShipmentAsync(ShipmentStatus.Packed);
        var request = NewDispatchRequest(FakeGroundCarrierGateway.Key) with
        {
            OverrideQuote = new CarrierQuoteOverride(PriceAmount: 99.99m, PriceCurrency: "USD", EstimatedDeliveryDays: 2),
        };

        var response = await CreateAuthenticatedClient().PostAsJsonAsync($"/{shipmentId}/dispatch", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DispatchShipmentResponse>();
        Assert.NotNull(body);
        Assert.Equal(99.99m, body.QuotedPriceAmount);
        Assert.Equal("USD", body.QuotedPriceCurrency);
    }

    private static DispatchShipmentRequest NewDispatchRequest(string carrierKey)
        => new(
            CarrierKey: carrierKey,
            ShippingAddress: new ShippingAddressDto(
                Recipient: "Jane Doe",
                Line1: "1 Main St",
                Line2: null,
                City: "Austin",
                State: "TX",
                PostalCode: "78701",
                Country: "US"),
            OverrideQuote: null);

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
