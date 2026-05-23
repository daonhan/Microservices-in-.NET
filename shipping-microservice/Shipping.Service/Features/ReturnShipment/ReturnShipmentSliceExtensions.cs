namespace Shipping.Service.Features.ReturnShipment;

internal static class ReturnShipmentSliceExtensions
{
    public static IServiceCollection AddReturnShipmentSlice(this IServiceCollection services)
    {
        services.AddScoped<ReturnShipmentHandler>();
        return services;
    }
}
