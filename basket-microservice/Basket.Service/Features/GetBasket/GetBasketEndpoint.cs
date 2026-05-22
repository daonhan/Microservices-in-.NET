using Basket.Service.Domain;

namespace Basket.Service.Features.GetBasket;

internal static class GetBasketEndpoint
{
    public static IEndpointRouteBuilder MapGetBasket(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapGet("/{customerId}", HandleAsync);
        return routeBuilder;
    }

    internal static Task<CustomerBasket> HandleAsync(GetBasketHandler handler, string customerId)
        => handler.HandleAsync(customerId);
}
