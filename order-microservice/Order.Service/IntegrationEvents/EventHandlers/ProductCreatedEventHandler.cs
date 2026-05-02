using System.Globalization;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Order.Service.IntegrationEvents.Events;

namespace Order.Service.IntegrationEvents.EventHandlers;

internal class ProductCreatedEventHandler : IEventHandler<ProductCreatedEvent>
{
    private readonly IDistributedCache _cache;
    private readonly DistributedCacheEntryOptions _cacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromHours(24)
    };

    public ProductCreatedEventHandler(IDistributedCache cache)
    {
        _cache = cache;
    }

    public Task Handle(ProductCreatedEvent @event)
    {
        var key = @event.ProductId.ToString(CultureInfo.InvariantCulture);
        var value = @event.Price.ToString(CultureInfo.InvariantCulture);
        return _cache.SetStringAsync(key, value, _cacheEntryOptions);
    }
}
