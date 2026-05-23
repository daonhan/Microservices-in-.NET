namespace Inventory.Service.Features.GetStockMovements;

internal static class GetStockMovementsEndpoint
{
    public static IEndpointRouteBuilder MapGetStockMovements(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{productId:int}/movements", HandleAsync).RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(GetStockMovementsHandler handler, int productId)
    {
        var response = await handler.HandleAsync(productId);
        return TypedResults.Ok(response);
    }
}
