using Order.Service.Domain.Abstractions;

namespace Order.Service.Features.CancelOrder;

internal static class CancelOrderEndpoint
{
    public static IEndpointRouteBuilder MapCancelOrder(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{customerId}/{orderId}/cancel", HandleAsync);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(IOrderStore orderStore, string customerId, string orderId)
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

        return TypedResults.Ok(new CancelOrderResponse(order.OrderId, order.CustomerId, order.OrderDate, order.Status.ToString()));
    }
}
