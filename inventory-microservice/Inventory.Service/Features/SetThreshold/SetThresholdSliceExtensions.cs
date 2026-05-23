namespace Inventory.Service.Features.SetThreshold;

internal static class SetThresholdSliceExtensions
{
    public static IServiceCollection AddSetThresholdSlice(this IServiceCollection services)
    {
        services.AddScoped<SetThresholdHandler>();
        return services;
    }
}
