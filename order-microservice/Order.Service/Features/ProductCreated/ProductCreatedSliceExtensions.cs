using ECommerce.Shared.Infrastructure.EventBus;
using Order.Service.Contracts.Integration;

namespace Order.Service.Features.ProductCreated;

internal static class ProductCreatedSliceExtensions
{
    public static IServiceCollection AddProductCreatedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ProductCreatedEvent, ProductCreatedEventHandler>();
        return services;
    }
}
