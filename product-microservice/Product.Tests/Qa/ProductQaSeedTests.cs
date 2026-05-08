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

    [Fact]
    public async Task GivenProductModelCreated_WhenReadingSeededProducts_ThenDeclineProductHasCents99Price()
    {
        var options = new DbContextOptionsBuilder<ProductContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ProductContext(options);
        await context.Database.EnsureCreatedAsync();

        var product = await context.Products.SingleAsync(p => p.Id == QaPersonas.ProductDeclineId);
        var cents = (int)Math.Round((product.Price - Math.Truncate(product.Price)) * 100m);

        Assert.Equal(QaPersonas.ProductDeclineName, product.Name);
        Assert.Equal(QaPersonas.ProductDeclinePrice, product.Price);
        Assert.Equal(99, cents);
    }

    [Fact]
    public async Task GivenProductModelCreated_WhenReadingSeededProducts_ThenZeroStockProductExists()
    {
        var options = new DbContextOptionsBuilder<ProductContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ProductContext(options);
        await context.Database.EnsureCreatedAsync();

        var product = await context.Products.SingleAsync(p => p.Id == QaPersonas.ProductZeroStockId);

        Assert.Equal(QaPersonas.ProductZeroStockName, product.Name);
        Assert.Equal(QaPersonas.ProductZeroStockPrice, product.Price);
    }

    [Fact]
    public async Task GivenProductModelCreated_WhenReadingSeededProducts_ThenLowStockAndRestockTargetExist()
    {
        var options = new DbContextOptionsBuilder<ProductContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ProductContext(options);
        await context.Database.EnsureCreatedAsync();

        var lowStock = await context.Products.SingleAsync(p => p.Id == QaPersonas.ProductLowStockId);
        var restockTarget = await context.Products.SingleAsync(p => p.Id == QaPersonas.ProductRestockTargetId);

        Assert.Equal(QaPersonas.ProductLowStockName, lowStock.Name);
        Assert.Equal(QaPersonas.ProductLowStockPrice, lowStock.Price);
        Assert.Equal(QaPersonas.ProductRestockTargetName, restockTarget.Name);
        Assert.Equal(QaPersonas.ProductRestockTargetPrice, restockTarget.Price);
    }
}
