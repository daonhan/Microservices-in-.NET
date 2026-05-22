using Basket.Service.Domain.Abstractions;
using ECommerce.Shared.Observability.Metrics;

namespace Basket.Service.Features.DeleteBasketProduct;

internal sealed class DeleteBasketProductHandler
{
    private readonly IBasketStore _basketStore;
    private readonly MetricFactory _metricFactory;

    public DeleteBasketProductHandler(IBasketStore basketStore, MetricFactory metricFactory)
    {
        _basketStore = basketStore;
        _metricFactory = metricFactory;
    }

    public async Task<IResult> HandleAsync(string customerId, string productId)
    {
        var customerBasket = await _basketStore.GetBasketByCustomerId(customerId);

        customerBasket.RemoveBasketProduct(productId);

        await _basketStore.UpdateCustomerBasket(customerBasket);

        _metricFactory.Counter("basket-updates", "updates").Add(1);
        _metricFactory.Counter("basket-products-removed", "products").Add(1);
        _metricFactory.Histogram("basket-size", "products").Record(customerBasket.Products.Count());

        return TypedResults.NoContent();
    }
}
