using Shipping.Service.Carriers;
using Shipping.Service.Models;

namespace Shipping.Tests.Carriers;

public class RateShoppingServiceTests
{
    [Fact]
    public async Task GetRankedQuotesAsync_OrdersByCheapestThenFastest()
    {
        var service = new RateShoppingService(new ICarrierGateway[]
        {
            new FakeExpressCarrierGateway(),
            new FakeGroundCarrierGateway(),
        });

        var quotes = await service.GetRankedQuotesAsync(new ShipmentQuoteRequest(
            ShipmentId: Guid.NewGuid(),
            WarehouseId: 1,
            Destination: new ShippingAddress("Jane", "1 Main", null, "Austin", "TX", "78701", "US"),
            TotalQuantity: 2));

        Assert.Equal(2, quotes.Count);
        // Ground is cheaper, should be first.
        Assert.Equal(FakeGroundCarrierGateway.Key, quotes[0].CarrierKey);
        Assert.Equal(FakeExpressCarrierGateway.Key, quotes[1].CarrierKey);
        Assert.True(quotes[0].Price.Amount <= quotes[1].Price.Amount);
    }

    [Fact]
    public void FindCarrier_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        var service = new RateShoppingService(new ICarrierGateway[]
        {
            new FakeExpressCarrierGateway(),
            new FakeGroundCarrierGateway(),
        });

        Assert.NotNull(service.FindCarrier(FakeExpressCarrierGateway.Key));
        Assert.NotNull(service.FindCarrier(FakeExpressCarrierGateway.Key.ToUpperInvariant()));
        Assert.Null(service.FindCarrier("unknown-carrier"));
    }
}
