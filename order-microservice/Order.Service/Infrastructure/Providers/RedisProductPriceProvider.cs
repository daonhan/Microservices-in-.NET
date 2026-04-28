using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Order.Service.Models;

namespace Order.Service.Infrastructure.Providers;

public class RedisProductPriceProvider : IProductPriceProvider
{
    private readonly IDistributedCache _cache;

    public RedisProductPriceProvider(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<Dictionary<string, decimal>> GetUnitPricesAsync(IEnumerable<string> productIds)
    {
        var unitPrices = new Dictionary<string, decimal>(StringComparer.Ordinal);
        
        foreach (var productId in productIds)
        {
            var cached = await _cache.GetStringAsync(productId)
                ?? throw new InvalidOperationException(
                    $"Product price not found in cache for product {productId}");
            
            unitPrices[productId] = decimal.Parse(cached, CultureInfo.InvariantCulture);
        }

        return unitPrices;
    }
}
