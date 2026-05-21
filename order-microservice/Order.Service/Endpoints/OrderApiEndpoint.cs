using Order.Service.ApiModels;
using Order.Service.Domain.Abstractions;

namespace Order.Service.Endpoints;

public static class OrderApiEndpoint
{
    public static void RegisterEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{customerId}/{orderId}", GetOrder);
        routeBuilder.MapPost("/{customerId}/{orderId}/cancel", CancelOrder);
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

    internal static async Task<IResult> CancelOrder(IOrderStore orderStore, string customerId, string orderId)
    {
        var order = await orderStore.GetCustomerOrderById(customerId, orderId);

        if (order is null)
        {
            return TypedResults.NotFound("Order not found for customer");
        }

        if (!order.TryCancel())
        {
            return TypedResults.BadRequest("Order cannot be cancelled in its current state");
        }

        await orderStore.ExecuteAsync(() => Task.CompletedTask);

        return TypedResults.Ok(new GetOrderResponse(order.OrderId, order.CustomerId, order.OrderDate, order.Status.ToString()));
    }
}
