namespace Inventory.Service.Features.GetStockItem;

internal static class GetStockItemSliceExtensions
{
    public static IServiceCollection AddGetStockItemSlice(this IServiceCollection services)
    {
        services.AddScoped<GetStockItemHandler>();
        return services;
    }
}
