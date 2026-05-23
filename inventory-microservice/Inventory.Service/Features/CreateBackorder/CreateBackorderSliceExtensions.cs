namespace Inventory.Service.Features.CreateBackorder;

internal static class CreateBackorderSliceExtensions
{
    public static IServiceCollection AddCreateBackorderSlice(this IServiceCollection services)
    {
        services.AddScoped<CreateBackorderHandler>();
        return services;
    }
}
