using System.Net;
using System.Net.Http.Json;
using Product.Service.Features.GetProduct;

namespace Product.Tests.Features.GetProduct;

public class GetProductTests : IntegrationTestBase
{
    public GetProductTests(ProductWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetProduct_WhenNoProductExists_ThenReturnsNotFound()
    {
        // Act
        var response = await HttpClient.GetAsync("/999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProduct_WhenProductExists_ThenReturnsProduct()
    {
        // Arrange
        var product = new Product.Service.Domain.Product("Integration Test Shoe", 99.99M, 1, "Test description");

        ProductContext.Products.Add(product);
        await ProductContext.SaveChangesAsync();

        // Act
        var response = await HttpClient.GetAsync($"/{product.Id}");

        // Assert
        response.EnsureSuccessStatusCode();

        var getProductResponse = await response.Content.ReadFromJsonAsync<GetProductResponse>();

        Assert.NotNull(getProductResponse);
        Assert.Equal(product.Id, getProductResponse.Id);
        Assert.Equal("Integration Test Shoe", getProductResponse.Name);
    }
}
