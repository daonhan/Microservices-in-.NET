namespace Shipping.Service.Features.CancelShipment;

internal static class CancelShipmentSliceExtensions
{
    public static IServiceCollection AddCancelShipmentSlice(this IServiceCollection services)
    {
        services.AddScoped<CancelShipmentHandler>();
        return services;
    }
}
