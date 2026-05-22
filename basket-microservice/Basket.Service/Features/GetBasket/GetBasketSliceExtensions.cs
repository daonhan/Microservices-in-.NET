namespace Basket.Service.Features.GetBasket;

internal static class GetBasketSliceExtensions
{
    public static IServiceCollection AddGetBasketSlice(this IServiceCollection services)
    {
        services.AddScoped<GetBasketHandler>();
        return services;
    }
}
