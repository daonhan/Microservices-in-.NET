namespace Product.Service.Features.UpdateProduct;

internal static class UpdateProductEndpoint
{
    public static IEndpointRouteBuilder MapUpdateProduct(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/{productId}", HandleAsync).RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        UpdateProductHandler handler, int productId, UpdateProductRequest request)
    {
        var updated = await handler.HandleAsync(productId, request);

        return updated
            ? TypedResults.NoContent()
            : TypedResults.NotFound($"Product with id {productId} does not exist");
    }
}
