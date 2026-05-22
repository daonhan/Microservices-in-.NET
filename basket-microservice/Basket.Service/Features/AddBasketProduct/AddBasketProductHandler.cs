using Basket.Service.Domain;
using Basket.Service.Domain.Abstractions;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.Extensions.Caching.Distributed;

namespace Basket.Service.Features.AddBasketProduct;

internal sealed class AddBasketProductHandler
{
    private readonly IBasketStore _basketStore;
    private readonly IDistributedCache _cache;
    private readonly MetricFactory _metricFactory;

    public AddBasketProductHandler(IBasketStore basketStore, IDistributedCache cache, MetricFactory metricFactory)
    {
        _basketStore = basketStore;
        _cache = cache;
        _metricFactory = metricFactory;
    }

    public async Task<IResult> HandleAsync(string customerId, AddBasketProductRequest request)
    {
        var customerBasket = await _basketStore.GetBasketByCustomerId(customerId);

        var cachedPrice = await _cache.GetStringAsync(request.ProductId)
            ?? throw new InvalidOperationException($"Product price not found in cache for product {request.ProductId}");
        var cachedProductPrice = decimal.Parse(cachedPrice, System.Globalization.CultureInfo.InvariantCulture);

        customerBasket.AddBasketProduct(new BasketProduct(request.ProductId,
            request.ProductName, cachedProductPrice, request.Quantity));

        await _basketStore.UpdateCustomerBasket(customerBasket);

        _metricFactory.Counter("basket-updates", "updates").Add(1);
        _metricFactory.Counter("basket-products-added", "products").Add(1);
        _metricFactory.Histogram("basket-size", "products").Record(customerBasket.Products.Count());

        return TypedResults.NoContent();
    }
}
