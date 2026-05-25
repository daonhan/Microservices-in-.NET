using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.OrderCreated;

internal static class OrderCreatedSliceExtensions
{
    public static IServiceCollection AddOrderCreatedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<OrderCreatedEvent, OrderCreatedHandler>();
        return services;
    }
}
