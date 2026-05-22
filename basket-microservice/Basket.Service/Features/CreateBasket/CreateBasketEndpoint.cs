namespace Basket.Service.Features.CreateBasket;

internal static class CreateBasketEndpoint
{
    public static IEndpointRouteBuilder MapCreateBasket(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPost("/{customerId}", HandleAsync);
        return routeBuilder;
    }

    internal static Task<IResult> HandleAsync(CreateBasketHandler handler, string customerId, CreateBasketRequest request)
        => handler.HandleAsync(customerId, request);
}
