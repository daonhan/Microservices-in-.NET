using Product.Service.Domain.Events;
using Product.Service.Features.UpdateProduct;

namespace Product.Tests.Features.UpdateProduct;

public class ProductPriceUpdatedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesProductIdAndNewPrice()
    {
        var domainEvent = new ProductPriceChangedDomainEvent(42, 88.88m);

        var integrationEvent = new ProductPriceUpdatedIntegrationMap().Map(domainEvent);

        Assert.Equal(42, integrationEvent.ProductId);
        Assert.Equal(88.88m, integrationEvent.NewPrice);
    }
}
