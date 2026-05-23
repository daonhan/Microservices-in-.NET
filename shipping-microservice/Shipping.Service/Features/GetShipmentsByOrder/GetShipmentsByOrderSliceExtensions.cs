namespace Shipping.Service.Features.GetShipmentsByOrder;

internal static class GetShipmentsByOrderSliceExtensions
{
    public static IServiceCollection AddGetShipmentsByOrderSlice(this IServiceCollection services)
    {
        services.AddScoped<GetShipmentsByOrderHandler>();
        return services;
    }
}
