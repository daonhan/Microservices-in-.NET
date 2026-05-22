namespace Product.Service.Features.GetProduct;

internal static class GetProductSliceExtensions
{
    public static IServiceCollection AddGetProductSlice(this IServiceCollection services)
    {
        services.AddScoped<GetProductHandler>();
        return services;
    }
}
