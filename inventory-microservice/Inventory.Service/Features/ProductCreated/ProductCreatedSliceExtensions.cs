using ECommerce.Shared.Infrastructure.EventBus;
using Inventory.Service.Contracts.Integration;

namespace Inventory.Service.Features.ProductCreated;

internal static class ProductCreatedSliceExtensions
{
    public static IServiceCollection AddProductCreatedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ProductCreatedEvent, ProductCreatedHandler>();
        return services;
    }
}
