namespace Inventory.Service.Features.ReserveByHttp;

internal static class ReserveByHttpSliceExtensions
{
    public static IServiceCollection AddReserveByHttpSlice(this IServiceCollection services)
    {
        services.AddScoped<ReserveByHttpHandler>();
        return services;
    }
}
