using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Product.Service.Infrastructure.Data.EntityFramework;

namespace Product.Tests.Qa;

public class ProductQaSeedTests
{
    [Fact]
    public async Task GivenProductModelCreated_WhenReadingSeededProducts_ThenProductHappyExists()
    {
        var options = new DbContextOptionsBuilder<ProductContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ProductContext(options);
        await context.Database.EnsureCreatedAsync();

        var product = await context.Products.SingleAsync(p => p.Id == QaPersonas.ProductHappyId);

        Assert.Equal(QaPersonas.ProductHappyName, product.Name);
        Assert.Equal(QaPersonas.ProductHappyPrice, product.Price);
        Assert.Equal(1, product.ProductTypeId);
    }
}
