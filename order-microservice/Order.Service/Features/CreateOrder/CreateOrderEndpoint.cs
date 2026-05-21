namespace Order.Service.Features.CreateOrder;

internal static class CreateOrderEndpoint
{
    public static IEndpointRouteBuilder MapCreateOrder(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{customerId}", HandleAsync);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        CreateOrderHandler handler, string customerId, CreateOrderRequest request)
    {
        var order = await handler.HandleAsync(customerId, request);
        return TypedResults.Created($"{order.CustomerId}/{order.OrderId}");
    }
}
