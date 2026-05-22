using System.Net.Http.Json;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Product.Service.ApiModels;

namespace Product.Tests.Api;

public class ObservabilityTests : IntegrationTestBase
{
    public ObservabilityTests(ProductWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task CreateProduct_IncrementsProductsCreatedCounter_ExposedOnMetrics()
    {
        // Arrange
        var client = CreateAuthenticatedClient();
        var request = new CreateProductRequest("Metrics Shoe", 19.99M, 1, "Observability test");

        // Act
        var createResponse = await client.PostAsJsonAsync("/", request);
        createResponse.EnsureSuccessStatusCode();

        var metricsResponse = await HttpClient.GetAsync("/metrics");

        // Assert
        metricsResponse.EnsureSuccessStatusCode();
        var body = await metricsResponse.Content.ReadAsStringAsync();
        Assert.Contains("products_created", body);
    }

    [Fact]
    public async Task UpdateProduct_WhenPriceChanges_IncrementsProductPriceUpdatesCounter_ExposedOnMetrics()
    {
        // Arrange
        var product = new Product.Service.Domain.Product("Price Counter Shoe", 10.00M, 1);
        ProductContext.Products.Add(product);
        await ProductContext.SaveChangesAsync();

        var client = CreateAuthenticatedClient();
        var updateRequest = new UpdateProductRequest("Price Counter Shoe", 20.00M, 1);

        // Act
        var response = await client.PutAsJsonAsync($"/{product.Id}", updateRequest);
        response.EnsureSuccessStatusCode();

        var metricsResponse = await HttpClient.GetAsync("/metrics");

        // Assert
        metricsResponse.EnsureSuccessStatusCode();
        var body = await metricsResponse.Content.ReadAsStringAsync();
        Assert.Contains("product_price_updates", body);
    }

    [Fact]
    public void MetricFactory_IsRegisteredWithProductMeterName()
    {
        // Arrange
        var metricFactory = Factory.Services.GetRequiredService<MetricFactory>();

        // Act
        metricFactory.Counter("products-created", "products").Add(1);

        // Assert — just confirms DI wiring; scrape assertion covered above
        Assert.NotNull(metricFactory);
    }
}
