namespace Product.Service.Features.GetProduct;

internal static class GetProductEndpoint
{
    public static IEndpointRouteBuilder MapGetProduct(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{productId}", HandleAsync);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(GetProductHandler handler, int productId)
    {
        var response = await handler.HandleAsync(productId);

        return response is null
            ? TypedResults.NotFound("Product not found")
            : TypedResults.Ok(response);
    }
}
