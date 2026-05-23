namespace Inventory.Service.Features.GetStockItem;

internal static class GetStockItemEndpoint
{
    public static IEndpointRouteBuilder MapGetStockItem(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{productId:int}", HandleAsync);
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(GetStockItemHandler handler, int productId)
    {
        var response = await handler.HandleAsync(productId);

        if (response is null)
        {
            return TypedResults.NotFound($"Stock item for product {productId} not found");
        }

        return TypedResults.Ok(response);
    }
}
