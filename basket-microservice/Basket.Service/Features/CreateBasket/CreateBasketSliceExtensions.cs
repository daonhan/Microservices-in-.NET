namespace Basket.Service.Features.CreateBasket;

internal static class CreateBasketSliceExtensions
{
    public static IServiceCollection AddCreateBasketSlice(this IServiceCollection services)
    {
        services.AddScoped<CreateBasketHandler>();
        return services;
    }
}
