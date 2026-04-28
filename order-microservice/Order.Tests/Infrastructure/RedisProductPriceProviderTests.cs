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

        var provider = new RedisProductPriceProvider(cache);

        // Act
        var prices = await provider.GetUnitPricesAsync(["prod-test-1", "prod-test-2"]);

        // Assert
        Assert.Equal(99.99m, prices["prod-test-1"]);
        Assert.Equal(149.50m, prices["prod-test-2"]);
    }

    [Fact]
    public async Task GetUnitPricesAsync_WhenPriceMissing_ThrowsException()
    {
        // Arrange
        var cache = Factory.Services.GetRequiredService<IDistributedCache>();
        var provider = new RedisProductPriceProvider(cache);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            provider.GetUnitPricesAsync(["missing-product-id-" + Guid.NewGuid()]));
            
        Assert.Contains("Product price not found", exception.Message);
    }
}
