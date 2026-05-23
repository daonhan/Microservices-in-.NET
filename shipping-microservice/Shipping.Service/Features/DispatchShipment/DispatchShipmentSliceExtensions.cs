namespace Shipping.Service.Features.DispatchShipment;

internal static class DispatchShipmentSliceExtensions
{
    public static IServiceCollection AddDispatchShipmentSlice(this IServiceCollection services)
    {
        services.AddScoped<DispatchShipmentHandler>();
        return services;
    }
}
