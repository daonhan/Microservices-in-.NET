namespace Inventory.Service.Features.Restock;

internal static class RestockSliceExtensions
{
    public static IServiceCollection AddRestockSlice(this IServiceCollection services)
    {
        services.AddScoped<RestockHandler>();
        return services;
    }
}
