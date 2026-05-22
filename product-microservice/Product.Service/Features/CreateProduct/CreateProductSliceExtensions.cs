using Product.Service.Infrastructure.Outbox;

namespace Product.Service.Features.CreateProduct;

internal static class CreateProductSliceExtensions
{
    public static IServiceCollection AddCreateProductSlice(this IServiceCollection services)
    {
        services.AddScoped<CreateProductHandler>();
        services.AddScoped<IIntegrationMap, ProductCreatedIntegrationMap>();
        return services;
    }
}
