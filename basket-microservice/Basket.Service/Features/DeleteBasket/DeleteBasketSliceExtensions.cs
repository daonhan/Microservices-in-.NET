namespace Basket.Service.Features.DeleteBasket;

internal static class DeleteBasketSliceExtensions
{
    public static IServiceCollection AddDeleteBasketSlice(this IServiceCollection services)
    {
        services.AddScoped<DeleteBasketHandler>();
        return services;
    }
}
