using Shipping.Service.Carriers;
using Shipping.Service.Models;

namespace Shipping.Tests.Carriers;

public class CarrierGatewayContractTests
{
    public static TheoryData<ICarrierGateway> Carriers => new()
    {
        new FakeExpressCarrierGateway(),
        new FakeGroundCarrierGateway(),
    };

    private static ShipmentQuoteRequest NewQuoteRequest() => new(
        ShipmentId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        WarehouseId: 1,
        Destination: new ShippingAddress("Jane Doe", "1 Main St", null, "Austin", "TX", "78701", "US"),
        TotalQuantity: 3);

    private static ShipmentDispatchRequest NewDispatchRequest() => new(
        ShipmentId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        WarehouseId: 1,
        Destination: new ShippingAddress("Jane Doe", "1 Main St", null, "Austin", "TX", "78701", "US"),
        TotalQuantity: 3);

    [Theory]
    [MemberData(nameof(Carriers))]
    public void CarrierKey_And_Name_AreNonEmpty(ICarrierGateway carrier)
    {
        Assert.False(string.IsNullOrWhiteSpace(carrier.CarrierKey));
        Assert.False(string.IsNullOrWhiteSpace(carrier.CarrierName));
    }

    [Theory]
    [MemberData(nameof(Carriers))]
    public async Task QuoteAsync_ReturnsPositivePriceAndDeliveryDays(ICarrierGateway carrier)
    {
        var quote = await carrier.QuoteAsync(NewQuoteRequest());

        Assert.Equal(carrier.CarrierKey, quote.CarrierKey);
        Assert.Equal(carrier.CarrierName, quote.CarrierName);
        Assert.True(quote.Price.Amount > 0);
        Assert.False(string.IsNullOrWhiteSpace(quote.Price.Currency));
        Assert.True(quote.EstimatedDeliveryDays > 0);
    }

    [Theory]
    [MemberData(nameof(Carriers))]
    public async Task QuoteAsync_IsDeterministic_ForSameRequest(ICarrierGateway carrier)
    {
        var a = await carrier.QuoteAsync(NewQuoteRequest());
        var b = await carrier.QuoteAsync(NewQuoteRequest());

        Assert.Equal(a.Price.Amount, b.Price.Amount);
        Assert.Equal(a.EstimatedDeliveryDays, b.EstimatedDeliveryDays);
    }

    [Theory]
    [MemberData(nameof(Carriers))]
    public async Task DispatchAsync_ReturnsTrackingAndLabel(ICarrierGateway carrier)
    {
        var result = await carrier.DispatchAsync(NewDispatchRequest());

        Assert.False(string.IsNullOrWhiteSpace(result.TrackingNumber));
        Assert.False(string.IsNullOrWhiteSpace(result.LabelRef));
    }

    [Theory]
    [MemberData(nameof(Carriers))]
    public async Task DispatchAsync_IsDeterministic_ForSameShipment(ICarrierGateway carrier)
    {
        var a = await carrier.DispatchAsync(NewDispatchRequest());
        var b = await carrier.DispatchAsync(NewDispatchRequest());

        Assert.Equal(a.TrackingNumber, b.TrackingNumber);
    }

    [Theory]
    [MemberData(nameof(Carriers))]
    public async Task GetStatusAsync_ReturnsKnownStatusCode(ICarrierGateway carrier)
    {
        var status = await carrier.GetStatusAsync("any-tracking");

        Assert.True(Enum.IsDefined(status.Code));
    }

    [Fact]
    public async Task Express_IsMoreExpensive_And_FasterThan_Ground()
    {
        var express = new FakeExpressCarrierGateway();
        var ground = new FakeGroundCarrierGateway();

        var expressQuote = await express.QuoteAsync(NewQuoteRequest());
        var groundQuote = await ground.QuoteAsync(NewQuoteRequest());

        Assert.True(expressQuote.Price.Amount > groundQuote.Price.Amount);
        Assert.True(expressQuote.EstimatedDeliveryDays < groundQuote.EstimatedDeliveryDays);
    }
}
