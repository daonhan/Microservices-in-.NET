using Product.Service.Domain.Events;

namespace Product.Tests.Domain;

public class ProductTests
{
    [Fact]
    public void Given_NewProduct_When_Constructed_Then_RaisesProductCreatedDomainEvent()
    {
        var product = new Service.Domain.Product("Running Shoe", 49.99M, 1, "A test shoe");

        var created = Assert.IsType<ProductCreatedDomainEvent>(Assert.Single(product.DequeueDomainEvents()));
        Assert.Same(product, created.Product);
        Assert.Equal("Running Shoe", created.Product.Name);
        Assert.Equal(49.99M, created.Product.Price);
    }

    [Fact]
    public void Given_Product_When_ChangePriceToDifferentValue_Then_RaisesProductPriceChangedDomainEventOnce()
    {
        var product = new Service.Domain.Product("Running Shoe", 49.99M, 1);
        product.DequeueDomainEvents();

        product.ChangePrice(59.99M);

        var changed = Assert.IsType<ProductPriceChangedDomainEvent>(Assert.Single(product.DequeueDomainEvents()));
        Assert.Equal(59.99M, changed.NewPrice);
        Assert.Equal(59.99M, product.Price);
    }

    [Fact]
    public void Given_Product_When_ChangePriceToSameValue_Then_RaisesNoDomainEvent()
    {
        var product = new Service.Domain.Product("Running Shoe", 49.99M, 1);
        product.DequeueDomainEvents();

        product.ChangePrice(49.99M);

        Assert.Empty(product.DequeueDomainEvents());
        Assert.Equal(49.99M, product.Price);
    }

    [Fact]
    public void Given_Product_When_Renamed_Then_RaisesNoDomainEvent()
    {
        var product = new Service.Domain.Product("Running Shoe", 49.99M, 1);
        product.DequeueDomainEvents();

        product.Rename("Trail Shoe");

        Assert.Equal("Trail Shoe", product.Name);
        Assert.Empty(product.DequeueDomainEvents());
    }

    [Fact]
    public void Given_Product_When_DescriptionAndTypeChanged_Then_MutatesWithoutRaisingDomainEvent()
    {
        var product = new Service.Domain.Product("Running Shoe", 49.99M, 1);
        product.DequeueDomainEvents();

        product.ChangeDescription("Updated description");
        product.ChangeType(2);

        Assert.Equal("Updated description", product.Description);
        Assert.Equal(2, product.ProductTypeId);
        Assert.Empty(product.DequeueDomainEvents());
    }

    [Fact]
    public void Given_ProductProperties_When_Inspected_Then_HaveNoPublicSetters()
    {
        var publicSetters = typeof(Service.Domain.Product)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(publicSetters);
    }
}
