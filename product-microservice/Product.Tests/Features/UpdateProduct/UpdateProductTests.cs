using System.Net;
using System.Net.Http.Json;
using Product.Service.Contracts.Integration;
using Product.Service.Features.UpdateProduct;

namespace Product.Tests.Features.UpdateProduct;

public class UpdateProductTests : IntegrationTestBase
{
    public UpdateProductTests(ProductWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task UpdateProduct_WhenProductExists_ThenUpdatesProduct()
    {
        // Arrange
        var product = new Product.Service.Domain.Product("Original Shoe", 50.00M, 1);

        ProductContext.Products.Add(product);
        await ProductContext.SaveChangesAsync();

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
        var product = new Product.Service.Domain.Product("Event Test Shoe", 50.00M, 1);

        ProductContext.Products.Add(product);
        await ProductContext.SaveChangesAsync();

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
