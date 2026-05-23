namespace Inventory.Service.Features.CreateBackorder;

internal static class CreateBackorderEndpoint
{
    public static IEndpointRouteBuilder MapCreateBackorder(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{productId:int}/backorder", HandleAsync)
            .RequireAuthorization();
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        CreateBackorderHandler handler,
        int productId,
        BackorderRequestDto request)
    {
        if (request.Quantity <= 0)
        {
            return TypedResults.BadRequest("Quantity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            return TypedResults.BadRequest("CustomerId is required.");
        }

        var response = await handler.HandleAsync(productId, request);

        if (response is null)
        {
            return TypedResults.NotFound($"Stock item for product {productId} not found");
        }

        return TypedResults.Ok(response);
    }
}
