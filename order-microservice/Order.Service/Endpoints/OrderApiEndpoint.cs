using System.Globalization;
using System.Transactions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Order.Service.ApiModels;
using Order.Service.Infrastructure.Data;
using Order.Service.IntegrationEvents.Events;

namespace Order.Service.Endpoints;

public static class OrderApiEndpoint
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{customerId}", CreateOrder);
        routeBuilder.MapGet("/{customerId}/{orderId}", GetOrder);
    }

    internal static async Task<IResult> CreateOrder(IOutboxStore outboxStore,
        IOrderStore orderStore, IDistributedCache cache, MetricFactory metricFactory,
        string customerId, CreateOrderRequest request)
    {
        var order = new Models.Order
        {
            CustomerId = customerId
        };

        foreach (var product in request.OrderProducts)
        {
            order.AddOrderProduct(product.ProductId, product.Quantity);
        }

        var unitPrices = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var product in order.OrderProducts.DistinctBy(p => p.ProductId))
        {
            var cached = await cache.GetStringAsync(product.ProductId)
                ?? throw new InvalidOperationException(
                    $"Product price not found in cache for product {product.ProductId}");
            unitPrices[product.ProductId] = decimal.Parse(cached, CultureInfo.InvariantCulture);
        }

        await outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            await orderStore.CreateOrder(order);

            var items = order.OrderProducts
                .Select(p => new OrderItem(p.ProductId, p.Quantity, unitPrices[p.ProductId]))
                .ToList();

            await outboxStore.AddOutboxEvent(new OrderCreatedEvent(order.OrderId, customerId, items));

            scope.Complete();
        });

        var orderCounter = metricFactory.Counter("total-orders", "Orders");
        orderCounter.Add(1);

        var productsPerOrderHistogram = metricFactory.Histogram("products-per-order", "Products");
        productsPerOrderHistogram.Record(order.OrderProducts.DistinctBy(p => p.ProductId).Count());

        return TypedResults.Created($"{order.CustomerId}/{order.OrderId}");
    }

    internal static async Task<IResult> GetOrder(IOrderStore orderStore, string customerId, string orderId)
    {
        var order = await orderStore.GetCustomerOrderById(customerId, orderId);

        if (order is null)
        {
            return TypedResults.NotFound("Order not found for customer");
        }

        return TypedResults.Ok(new GetOrderResponse(order.OrderId, order.CustomerId, order.OrderDate, order.Status.ToString()));
    }
}
