using Basket.Service.ApiModels;
using Basket.Service.Infrastructure.Data;
using Basket.Service.Models;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.Extensions.Caching.Distributed;

namespace Basket.Service.Endpoints;

public static class BasketApiEndpoints
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{customerId}", GetBasket);
        routeBuilder.MapPost("/{customerId}", CreateBasket);
        routeBuilder.MapPut("/{customerId}", AddBasketProduct);
        routeBuilder.MapDelete("/{customerId}/{productId}", DeleteBasketProduct);
        routeBuilder.MapDelete("/{customerId}", DeleteBasket);
    }

    internal static async Task<CustomerBasket> GetBasket(IBasketStore basketStore, string customerId)
        => await basketStore.GetBasketByCustomerId(customerId);

    internal static async Task<IResult> CreateBasket(IBasketStore basketStore, IDistributedCache cache,
        MetricFactory metricFactory,
        string customerId, CreateBasketRequest createBasketRequest)
    {
        var customerBasket = new CustomerBasket { CustomerId = customerId };

        var cachedPrice = await cache.GetStringAsync(createBasketRequest.ProductId)
            ?? throw new InvalidOperationException($"Product price not found in cache for product {createBasketRequest.ProductId}");
        var cachedProductPrice = decimal.Parse(cachedPrice, System.Globalization.CultureInfo.InvariantCulture);

        customerBasket.AddBasketProduct(
            new BasketProduct(createBasketRequest.ProductId,
                createBasketRequest.ProductName, cachedProductPrice));

        await basketStore.CreateCustomerBasket(customerBasket);

        RecordBasketUpdate(metricFactory, customerBasket, productsAdded: 1);

        return TypedResults.Created();
    }

    internal static async Task<IResult> AddBasketProduct(IBasketStore basketStore, IDistributedCache cache,
        MetricFactory metricFactory,
        string customerId, AddBasketProductRequest addProductRequest)
    {
        var customerBasket = await basketStore.GetBasketByCustomerId(customerId);

        var cachedPrice = await cache.GetStringAsync(addProductRequest.ProductId)
            ?? throw new InvalidOperationException($"Product price not found in cache for product {addProductRequest.ProductId}");
        var cachedProductPrice = decimal.Parse(cachedPrice, System.Globalization.CultureInfo.InvariantCulture);

        customerBasket.AddBasketProduct(new BasketProduct(addProductRequest.ProductId,
            addProductRequest.ProductName, cachedProductPrice, addProductRequest.Quantity));

        await basketStore.UpdateCustomerBasket(customerBasket);

        RecordBasketUpdate(metricFactory, customerBasket, productsAdded: 1);

        return TypedResults.NoContent();
    }

    internal static async Task<IResult> DeleteBasketProduct(IBasketStore basketStore,
        MetricFactory metricFactory,
        string customerId, string productId)
    {
        var customerBasket = await basketStore.GetBasketByCustomerId(customerId);

        customerBasket.RemoveBasketProduct(productId);

        await basketStore.UpdateCustomerBasket(customerBasket);

        RecordBasketUpdate(metricFactory, customerBasket, productsRemoved: 1);

        return TypedResults.NoContent();
    }

    internal static async Task<IResult> DeleteBasket(IBasketStore basketStore, string customerId)
    {
        await basketStore.DeleteCustomerBasket(customerId);

        return TypedResults.NoContent();
    }

    private static void RecordBasketUpdate(MetricFactory metricFactory, CustomerBasket basket,
        int productsAdded = 0, int productsRemoved = 0)
    {
        metricFactory.Counter("basket-updates", "updates").Add(1);

        if (productsAdded > 0)
        {
            metricFactory.Counter("basket-products-added", "products").Add(productsAdded);
        }

        if (productsRemoved > 0)
        {
            metricFactory.Counter("basket-products-removed", "products").Add(productsRemoved);
        }

        metricFactory.Histogram("basket-size", "products").Record(basket.Products.Count());
    }
}
