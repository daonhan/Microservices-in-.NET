namespace Product.Service.Features.ListProducts;

internal static class ListProductsEndpoint
{
    public static IEndpointRouteBuilder MapListProducts(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/", HandleAsync);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(ListProductsHandler handler)
    {
        var response = await handler.HandleAsync();

        return TypedResults.Ok(response);
    }
}
