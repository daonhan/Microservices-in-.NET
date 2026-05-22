using Basket.Service.Contracts.Integration;
using ECommerce.Shared.Infrastructure.EventBus;

namespace Basket.Service.Features.ProductPriceUpdated;

internal static class ProductPriceUpdatedSliceExtensions
{
    public static IServiceCollection AddProductPriceUpdatedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ProductPriceUpdatedEvent, ProductPriceUpdatedHandler>();
        return services;
    }
}
