using ECommerce.Shared.Observability.Metrics;
using Order.Service.Domain.Abstractions;

namespace Order.Service.Features.CreateOrder;

internal sealed class CreateOrderHandler
{
    private readonly IOrderStore _orderStore;
    private readonly IProductPriceProvider _priceProvider;
    private readonly MetricFactory _metricFactory;

    public CreateOrderHandler(IOrderStore orderStore, IProductPriceProvider priceProvider, MetricFactory metricFactory)
    {
        _orderStore = orderStore;
        _priceProvider = priceProvider;
        _metricFactory = metricFactory;
    }

    public async Task<Domain.Order> HandleAsync(string customerId, CreateOrderRequest request)
    {
        var order = new Domain.Order
        {
            CustomerId = customerId
        };

        foreach (var product in request.OrderProducts)
        {
            order.AddOrderProduct(product.ProductId, product.Quantity);
        }

        var uniqueProductIds = order.OrderProducts.Select(p => p.ProductId).Distinct().ToList();
        var unitPrices = await _priceProvider.GetUnitPricesAsync(uniqueProductIds);

        order.Submit(unitPrices);

        await _orderStore.ExecuteAsync(() => _orderStore.CreateOrder(order));

        var orderCounter = _metricFactory.Counter("total-orders", "Orders");
        orderCounter.Add(1);

        var productsPerOrderHistogram = _metricFactory.Histogram("products-per-order", "Products");
        productsPerOrderHistogram.Record(order.OrderProducts.DistinctBy(p => p.ProductId).Count());

        return order;
    }
}
