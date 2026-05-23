namespace Inventory.Service.Features.ListStockItems;

internal static class ListStockItemsEndpoint
{
    public static IEndpointRouteBuilder MapListStockItems(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/", HandleAsync).RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(ListStockItemsHandler handler)
    {
        var response = await handler.HandleAsync();
        return TypedResults.Ok(response);
    }
}
