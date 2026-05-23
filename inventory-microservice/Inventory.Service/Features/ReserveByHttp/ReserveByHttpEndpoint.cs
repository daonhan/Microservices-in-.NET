namespace Inventory.Service.Features.ReserveByHttp;

internal static class ReserveByHttpEndpoint
{
    public static IEndpointRouteBuilder MapReserveByHttp(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{productId:int}/reserve", HandleAsync)
            .RequireAuthorization("Administrator");
        return routeBuilder;
    }

    internal static async Task<IResult> HandleAsync(
        ReserveByHttpHandler handler,
        int productId,
        ReserveRequest request)
    {
        if (request.Quantity <= 0)
        {
            return TypedResults.BadRequest("Quantity must be greater than zero.");
        }

        if (request.OrderId == Guid.Empty)
        {
            return TypedResults.BadRequest("OrderId is required.");
        }

        var response = await handler.HandleAsync(productId, request);

        if (response is null)
        {
            return TypedResults.Conflict("Insufficient stock or unknown product.");
        }

        return TypedResults.Ok(response);
    }
}
