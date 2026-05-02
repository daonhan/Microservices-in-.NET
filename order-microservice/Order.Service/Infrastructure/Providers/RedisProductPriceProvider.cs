using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Order.Service.Models;

namespace Order.Service.Infrastructure.Providers;

public class RedisProductPriceProvider : IProductPriceProvider
{
    private static readonly DistributedCacheEntryOptions CacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromHours(24)
    };

    private readonly IDistributedCache _cache;
    private readonly IProductCatalogClient _catalogClient;

    public RedisProductPriceProvider(IDistributedCache cache, IProductCatalogClient catalogClient)
    {
        _cache = cache;
        _catalogClient = catalogClient;
    }

    public async Task<Dictionary<string, decimal>> GetUnitPricesAsync(IEnumerable<string> productIds)
    {
        var unitPrices = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var productId in productIds)
        {
            var cached = await _cache.GetStringAsync(productId);

            if (cached is not null)
            {
                unitPrices[productId] = decimal.Parse(cached, CultureInfo.InvariantCulture);
                continue;
            }

            // Cache miss: fall back to product service and back-fill the cache.
            var price = await _catalogClient.GetUnitPriceAsync(productId)
                ?? throw new InvalidOperationException(
                    $"Product price not found for product {productId}");

            await _cache.SetStringAsync(
                productId,
                price.ToString(CultureInfo.InvariantCulture),
                CacheEntryOptions);

            unitPrices[productId] = price;
        }

        return unitPrices;
    }
}
