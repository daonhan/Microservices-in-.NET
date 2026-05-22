using Product.Service.Infrastructure.Outbox;

namespace Product.Service.Features.UpdateProduct;

internal static class UpdateProductSliceExtensions
{
    public static IServiceCollection AddUpdateProductSlice(this IServiceCollection services)
    {
        services.AddScoped<UpdateProductHandler>();
        services.AddScoped<IIntegrationMap, ProductPriceUpdatedIntegrationMap>();
        return services;
    }
}
