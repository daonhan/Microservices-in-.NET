namespace Basket.Service.Features.DeleteBasket;

internal static class DeleteBasketEndpoint
{
    public static IEndpointRouteBuilder MapDeleteBasket(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapDelete("/{customerId}", HandleAsync);
        return routeBuilder;
    }

    internal static Task<IResult> HandleAsync(DeleteBasketHandler handler, string customerId)
        => handler.HandleAsync(customerId);
}
