namespace Shipping.Service.Features.DeliverShipment;

internal static class DeliverShipmentSliceExtensions
{
    public static IServiceCollection AddDeliverShipmentSlice(this IServiceCollection services)
    {
        services.AddScoped<DeliverShipmentHandler>();
        return services;
    }
}
