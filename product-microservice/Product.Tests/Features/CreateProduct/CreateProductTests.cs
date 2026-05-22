using System.Net.Http.Json;
using Product.Service.Features.CreateProduct;

namespace Product.Tests.Features.CreateProduct;

public class CreateProductTests : IntegrationTestBase
{
    public CreateProductTests(ProductWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task CreateProduct_WhenCalled_ThenCreatesProduct()
    {
        // Arrange
        var createProductRequest = new CreateProductRequest("Test Shoe", 49.99M, 1, "A test shoe");

        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/", createProductRequest);

        // Assert
        response.EnsureSuccessStatusCode();

        var locationHeader = response.Headers.FirstOrDefault(h =>
            string.Equals(h.Key, "Location", StringComparison.Ordinal)).Value.FirstOrDefault();

        Assert.NotNull(locationHeader);

        var productId = int.Parse(locationHeader, System.Globalization.CultureInfo.InvariantCulture);

        var product = ProductContext.Products.FirstOrDefault(p => p.Id == productId);
        Assert.NotNull(product);
        Assert.Equal("Test Shoe", product.Name);
        Assert.Equal(49.99M, product.Price);
    }
}
