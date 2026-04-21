using System.Net;
using System.Net.Http.Json;
using Product.Service.ApiModels;
using Product.Service.IntegrationEvents;

namespace Product.Tests.Api;

public class ProductApiTests : IntegrationTestBase
{
    public ProductApiTests(ProductWebApplicationFactory webApplicationFactory)
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

    [Fact]
    public async Task GetProduct_WhenProductExists_ThenReturnsProduct()
    {
        // Arrange
        var product = new Product.Service.Models.Product
        {
            Name = "Integration Test Shoe",
            Price = 99.99M,
            ProductTypeId = 1,
            Description = "Test description"
        };

        await ProductContext.CreateProduct(product);

        // Act
        var response = await HttpClient.GetAsync($"/{product.Id}");

        // Assert
        response.EnsureSuccessStatusCode();

        var getProductResponse = await response.Content.ReadFromJsonAsync<GetProductResponse>();

        Assert.NotNull(getProductResponse);
        Assert.Equal(product.Id, getProductResponse.Id);
        Assert.Equal("Integration Test Shoe", getProductResponse.Name);
    }

    [Fact]
    public async Task UpdateProduct_WhenProductExists_ThenUpdatesProduct()
    {
        // Arrange
        var product = new Product.Service.Models.Product
        {
            Name = "Original Shoe",
            Price = 50.00M,
            ProductTypeId = 1
        };

        await ProductContext.CreateProduct(product);

        var updateRequest = new UpdateProductRequest("Updated Shoe", 75.00M, 1, "Updated description");

        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PutAsJsonAsync($"/{product.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProduct_WhenPriceChanges_ThenProductPriceUpdatedEventPublished()
    {
        // Arrange
        var product = new Product.Service.Models.Product
        {
            Name = "Event Test Shoe",
            Price = 50.00M,
            ProductTypeId = 1
        };

        await ProductContext.CreateProduct(product);

        Subscribe<ProductPriceUpdatedEvent>();

        var updateRequest = new UpdateProductRequest("Event Test Shoe", 75.00M, 1);

        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PutAsJsonAsync($"/{product.Id}", updateRequest);

        // Assert
        response.EnsureSuccessStatusCode();

        SpinWait.SpinUntil(() =>
            ReceivedEvents.OfType<ProductPriceUpdatedEvent>().Any(e => e.ProductId == product.Id && e.NewPrice == 75.00M),
            TimeSpan.FromSeconds(5));

        var receivedEvent = ReceivedEvents
            .OfType<ProductPriceUpdatedEvent>()
            .FirstOrDefault(e => e.ProductId == product.Id);
        Assert.NotNull(receivedEvent);
        Assert.Equal(75.00M, receivedEvent.NewPrice);
    }
}
