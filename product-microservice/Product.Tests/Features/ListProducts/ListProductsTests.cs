using System.Net;
using System.Net.Http.Json;
using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Product.Service.Features.ListProducts;
using Product.Service.Infrastructure.Data.EntityFramework;

namespace Product.Tests.Features.ListProducts;

public class ListProductsHandlerTests
{
    [Fact]
    public async Task ListProducts_WhenDatabaseEmpty_ThenReturnsEmptyList()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ProductContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ProductContext(options);
        var handler = new ListProductsHandler(context);

        // Act
        var products = await handler.HandleAsync();

        // Assert
        Assert.Empty(products);
    }
}

public class ListProductsTests : IntegrationTestBase
{
    public ListProductsTests(ProductWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task ListProducts_WhenProductsSeeded_ThenReturnsAllSeededPersonas()
    {
        // Act
        var response = await HttpClient.GetAsync("/");

        // Assert
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<List<ListProductsResponseItem>>();

        Assert.NotNull(products);

        var happy = products.SingleOrDefault(p => p.Id == QaPersonas.ProductHappyId);
        Assert.NotNull(happy);
        Assert.Equal(QaPersonas.ProductHappyName, happy.Name);
        Assert.Equal(QaPersonas.ProductHappyPrice, happy.Price);
        Assert.Equal("Shoes", happy.ProductType);

        Assert.Contains(products, p => p.Id == QaPersonas.ProductDeclineId);
        Assert.Contains(products, p => p.Id == QaPersonas.ProductZeroStockId);
        Assert.Contains(products, p => p.Id == QaPersonas.ProductLowStockId);
        Assert.Contains(products, p => p.Id == QaPersonas.ProductRestockTargetId);
    }

    [Fact]
    public async Task ListProducts_WhenNoAuthorizationHeader_ThenReturnsOk()
    {
        // Act
        var response = await HttpClient.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
