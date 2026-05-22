namespace Basket.Service.Features.AddBasketProduct;

internal static class AddBasketProductSliceExtensions
{
    public static IServiceCollection AddAddBasketProductSlice(this IServiceCollection services)
    {
        services.AddScoped<AddBasketProductHandler>();
        return services;
    }
}
