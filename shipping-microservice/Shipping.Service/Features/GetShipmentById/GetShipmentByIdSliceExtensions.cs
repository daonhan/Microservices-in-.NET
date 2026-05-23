namespace Shipping.Service.Features.GetShipmentById;

internal static class GetShipmentByIdSliceExtensions
{
    public static IServiceCollection AddGetShipmentByIdSlice(this IServiceCollection services)
    {
        services.AddScoped<GetShipmentByIdHandler>();
        return services;
    }
}
