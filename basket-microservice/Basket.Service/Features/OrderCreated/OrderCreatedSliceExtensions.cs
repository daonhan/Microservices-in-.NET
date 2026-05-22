using Basket.Service.Contracts.Integration;
using ECommerce.Shared.Infrastructure.EventBus;

namespace Basket.Service.Features.OrderCreated;

internal static class OrderCreatedSliceExtensions
{
    public static IServiceCollection AddOrderCreatedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<OrderCreatedEvent, OrderCreatedHandler>();
        return services;
    }
}
