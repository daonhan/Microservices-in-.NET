namespace Shipping.Service.Features.PackShipment;

internal static class PackShipmentSliceExtensions
{
    public static IServiceCollection AddPackShipmentSlice(this IServiceCollection services)
    {
        services.AddScoped<PackShipmentHandler>();
        return services;
    }
}
