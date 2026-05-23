namespace Inventory.Service.Features.SetThreshold;

internal static class SetThresholdEndpoint
{
    public static IEndpointRouteBuilder MapSetThreshold(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/{productId:int}/threshold", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        SetThresholdHandler handler,
        int productId,
        SetThresholdRequest request)
    {
        if (request.Threshold < 0)
        {
            return TypedResults.BadRequest("Threshold must be zero or greater.");
        }

        var response = await handler.HandleAsync(productId, request);

        if (response is null)
        {
            return TypedResults.NotFound($"Stock item for product {productId} not found");
        }

        return TypedResults.Ok(response);
    }
}
