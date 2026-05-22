namespace Basket.Service.Features.DeleteBasketProduct;

internal static class DeleteBasketProductEndpoint
{
    public static IEndpointRouteBuilder MapDeleteBasketProduct(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapDelete("/{customerId}/{productId}", HandleAsync);
        return routeBuilder;
    }

    internal static Task<IResult> HandleAsync(DeleteBasketProductHandler handler, string customerId, string productId)
        => handler.HandleAsync(customerId, productId);
}
