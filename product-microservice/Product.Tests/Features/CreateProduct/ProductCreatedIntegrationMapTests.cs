using Product.Service.Domain.Events;
using Product.Service.Features.CreateProduct;

namespace Product.Tests.Features.CreateProduct;

public class ProductCreatedIntegrationMapTests
{
    [Fact]
    public void Map_PreservesProductIdNameAndPrice()
    {
        var product = new Product.Service.Domain.Product("Mapped Shoe", 123.45m, 1, "A mapped shoe");
        var domainEvent = new ProductCreatedDomainEvent(product);

        var integrationEvent = new ProductCreatedIntegrationMap().Map(domainEvent);

        Assert.Equal(product.Id, integrationEvent.ProductId);
        Assert.Equal("Mapped Shoe", integrationEvent.Name);
        Assert.Equal(123.45m, integrationEvent.Price);
    }
}
