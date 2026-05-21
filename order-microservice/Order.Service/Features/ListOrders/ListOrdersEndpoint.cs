namespace Order.Service.Features.ListOrders;

internal static class ListOrdersEndpoint
{
    public static IEndpointRouteBuilder MapListOrders(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{customerId}", HandleAsync);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(ListOrdersHandler handler, string customerId)
    {
        var response = await handler.HandleAsync(customerId);
        return TypedResults.Ok(response);
    }
}
