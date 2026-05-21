namespace Order.Service.Features.GetOrder;

internal static class GetOrderSliceExtensions
{
    public static IServiceCollection AddGetOrderSlice(this IServiceCollection services)
    {
        services.AddScoped<GetOrderHandler>();
        return services;
    }
}
