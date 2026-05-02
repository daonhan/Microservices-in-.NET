using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Order.Service.Infrastructure.Providers;

namespace Order.Tests.Infrastructure;

public class RedisProductPriceProviderTests : IntegrationTestBase
{
    public RedisProductPriceProviderTests(OrderWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetUnitPricesAsync_WhenPricesExistInCache_ReturnsPrices()
    {
        // Arrange
        var cache = Factory.Services.GetRequiredService<IDistributedCache>();
        await cache.SetStringAsync("prod-test-1", "99.99");
        await cache.SetStringAsync("prod-test-2", "149.50");

        var provider = new RedisProductPriceProvider(cache, new StubCatalogClient(null));

        // Act
        var prices = await provider.GetUnitPricesAsync(["prod-test-1", "prod-test-2"]);

        // Assert
        Assert.Equal(99.99m, prices["prod-test-1"]);
        Assert.Equal(149.50m, prices["prod-test-2"]);
    }

    [Fact]
    public async Task GetUnitPricesAsync_WhenCacheMiss_FetchesFromCatalogAndBackfills()
    {
        // Arrange
        var cache = Factory.Services.GetRequiredService<IDistributedCache>();
        var key = "missing-product-id-" + Guid.NewGuid();
        var provider = new RedisProductPriceProvider(cache, new StubCatalogClient(42.42m));

        // Act
        var prices = await provider.GetUnitPricesAsync([key]);

        // Assert
        Assert.Equal(42.42m, prices[key]);
        Assert.Equal("42.42", await cache.GetStringAsync(key));
    }

    [Fact]
    public async Task GetUnitPricesAsync_WhenPriceMissingEverywhere_ThrowsException()
    {
        // Arrange
        var cache = Factory.Services.GetRequiredService<IDistributedCache>();
        var provider = new RedisProductPriceProvider(cache, new StubCatalogClient(null));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetUnitPricesAsync(["missing-product-id-" + Guid.NewGuid()]));

        Assert.Contains("Product price not found", exception.Message);
    }

    private sealed class StubCatalogClient : IProductCatalogClient
    {
        private readonly decimal? _price;
        public StubCatalogClient(decimal? price) => _price = price;
        public Task<decimal?> GetUnitPriceAsync(string productId, CancellationToken cancellationToken = default)
            => Task.FromResult(_price);
    }
}
