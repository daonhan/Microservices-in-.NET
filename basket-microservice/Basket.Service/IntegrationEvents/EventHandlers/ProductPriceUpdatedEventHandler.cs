using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace Basket.Service.IntegrationEvents.EventHandlers;

public class ProductPriceUpdatedEventHandler : IEventHandler<ProductPriceUpdatedEvent>
{
    private readonly IDistributedCache _cache;
    private readonly DistributedCacheEntryOptions _cacheEntryOptions;

    public ProductPriceUpdatedEventHandler(IDistributedCache cache)
    {
        _cache = cache;
        _cacheEntryOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(24)
        };
    }

    public async Task Handle(ProductPriceUpdatedEvent @event)
    {
        var existingProductPrice = await _cache.GetStringAsync(@event.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (existingProductPrice is null || !string.Equals(existingProductPrice, @event.NewPrice.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            await _cache.SetStringAsync(@event.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture), @event.NewPrice.ToString(System.Globalization.CultureInfo.InvariantCulture), _cacheEntryOptions);
        }
    }
}
