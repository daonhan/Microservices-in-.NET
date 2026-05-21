namespace Order.Service.Features.GetOrder;

internal static class GetOrderEndpoint
{
    public static IEndpointRouteBuilder MapGetOrder(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{customerId}/{orderId}", HandleAsync);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(GetOrderHandler handler, string customerId, string orderId)
    {
        var response = await handler.HandleAsync(customerId, orderId);

        if (response is null)
        {
            return TypedResults.NotFound("Order not found for customer");
        }

        return TypedResults.Ok(response);
    }
}
