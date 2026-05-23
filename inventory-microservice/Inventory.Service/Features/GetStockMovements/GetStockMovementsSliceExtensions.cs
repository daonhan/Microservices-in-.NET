namespace Inventory.Service.Features.GetStockMovements;

internal static class GetStockMovementsSliceExtensions
{
    public static IServiceCollection AddGetStockMovementsSlice(this IServiceCollection services)
    {
        services.AddScoped<GetStockMovementsHandler>();
        return services;
    }
}
