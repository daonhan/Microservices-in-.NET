namespace Basket.Service.Features.DeleteBasketProduct;

internal static class DeleteBasketProductSliceExtensions
{
    public static IServiceCollection AddDeleteBasketProductSlice(this IServiceCollection services)
    {
        services.AddScoped<DeleteBasketProductHandler>();
        return services;
    }
}
