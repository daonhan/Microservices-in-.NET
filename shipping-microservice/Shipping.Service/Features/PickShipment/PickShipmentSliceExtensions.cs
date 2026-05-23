namespace Shipping.Service.Features.PickShipment;

internal static class PickShipmentSliceExtensions
{
    public static IServiceCollection AddPickShipmentSlice(this IServiceCollection services)
    {
        services.AddScoped<PickShipmentHandler>();
        return services;
    }
}
