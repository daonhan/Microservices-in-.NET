namespace Product.Service.Features.ListProducts;

internal static class ListProductsSliceExtensions
{
    public static IServiceCollection AddListProductsSlice(this IServiceCollection services)
    {
        services.AddScoped<ListProductsHandler>();
        return services;
    }
}
