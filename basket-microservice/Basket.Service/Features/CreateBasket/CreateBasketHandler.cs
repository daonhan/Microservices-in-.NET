using Basket.Service.Domain;
using Basket.Service.Domain.Abstractions;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.Extensions.Caching.Distributed;

namespace Basket.Service.Features.CreateBasket;

internal sealed class CreateBasketHandler
{
    private readonly IBasketStore _basketStore;
    private readonly IDistributedCache _cache;
    private readonly MetricFactory _metricFactory;

    public CreateBasketHandler(IBasketStore basketStore, IDistributedCache cache, MetricFactory metricFactory)
    {
        _basketStore = basketStore;
        _cache = cache;
        _metricFactory = metricFactory;
    }

    public async Task<IResult> HandleAsync(string customerId, CreateBasketRequest request)
    {
        var customerBasket = new CustomerBasket { CustomerId = customerId };

        var cachedPrice = await _cache.GetStringAsync(request.ProductId)
            ?? throw new InvalidOperationException($"Product price not found in cache for product {request.ProductId}");
        var cachedProductPrice = decimal.Parse(cachedPrice, System.Globalization.CultureInfo.InvariantCulture);

        customerBasket.AddBasketProduct(
            new BasketProduct(request.ProductId, request.ProductName, cachedProductPrice));

        await _basketStore.CreateCustomerBasket(customerBasket);

        _metricFactory.Counter("basket-updates", "updates").Add(1);
        _metricFactory.Counter("basket-products-added", "products").Add(1);
        _metricFactory.Histogram("basket-size", "products").Record(customerBasket.Products.Count());

        return TypedResults.Created();
    }
}
