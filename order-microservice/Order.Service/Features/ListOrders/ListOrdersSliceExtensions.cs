namespace Order.Service.Features.ListOrders;

internal static class ListOrdersSliceExtensions
{
    public static IServiceCollection AddListOrdersSlice(this IServiceCollection services)
    {
        services.AddScoped<ListOrdersHandler>();
        return services;
    }
}
