using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
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

    internal static async Task<IResult> CreateOrder(IEventBus eventBus,
        IOrderStore orderStore,
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

        await orderStore.CreateOrder(order);

        await eventBus.PublishAsync(new OrderCreatedEvent(customerId));

        return TypedResults.Created($"{order.CustomerId}/{order.OrderId}");
    }

    internal static async Task<IResult> GetOrder(IOrderStore orderStore, string customerId, string orderId)
    {
        var order = await orderStore.GetCustomerOrderById(customerId, orderId);

        if (order is null)
        {
            return TypedResults.NotFound("Order not found for customer");
        }

        return TypedResults.Ok(new GetOrderResponse(order.OrderId, order.CustomerId, order.OrderDate));
    }
}
