using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Contracts.Integration;

namespace Payment.Service.Features.OrderCreated;

internal static class OrderCreatedSliceExtensions
{
    public static IServiceCollection AddOrderCreatedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<OrderCreatedEvent, OrderCreatedHandler>();
        return services;
    }
}
