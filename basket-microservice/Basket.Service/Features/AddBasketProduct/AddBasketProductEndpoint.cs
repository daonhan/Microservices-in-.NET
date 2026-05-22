namespace Basket.Service.Features.AddBasketProduct;

internal static class AddBasketProductEndpoint
{
    public static IEndpointRouteBuilder MapAddBasketProduct(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapPut("/{customerId}", HandleAsync);
        return routeBuilder;
    }

    internal static Task<IResult> HandleAsync(AddBasketProductHandler handler, string customerId, AddBasketProductRequest request)
        => handler.HandleAsync(customerId, request);
}
