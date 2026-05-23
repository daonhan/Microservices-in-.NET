namespace Shipping.Service.Features.ListShipments;

internal static class ListShipmentsSliceExtensions
{
    public static IServiceCollection AddListShipmentsSlice(this IServiceCollection services)
    {
        services.AddScoped<ListShipmentsHandler>();
        return services;
    }
}
