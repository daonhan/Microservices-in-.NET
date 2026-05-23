namespace Shipping.Service.Features.FailShipment;

internal static class FailShipmentSliceExtensions
{
    public static IServiceCollection AddFailShipmentSlice(this IServiceCollection services)
    {
        services.AddScoped<FailShipmentHandler>();
        return services;
    }
}
