namespace Inventory.Service.Features.Restock;

internal static class RestockEndpoint
{
    public static IEndpointRouteBuilder MapRestock(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{productId:int}/restock", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        RestockHandler handler,
        int productId,
        RestockRequest request)
    {
        if (request.Quantity <= 0)
        {
            return TypedResults.BadRequest("Quantity must be greater than zero.");
        }

        var response = await handler.HandleAsync(productId, request);

        if (response is null)
        {
            return TypedResults.NotFound($"Stock item for product {productId} not found");
        }

        return TypedResults.Ok(response);
    }
}
