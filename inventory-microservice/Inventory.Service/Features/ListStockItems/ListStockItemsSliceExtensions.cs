namespace Inventory.Service.Features.ListStockItems;

internal static class ListStockItemsSliceExtensions
{
    public static IServiceCollection AddListStockItemsSlice(this IServiceCollection services)
    {
        services.AddScoped<ListStockItemsHandler>();
        return services;
    }
}
