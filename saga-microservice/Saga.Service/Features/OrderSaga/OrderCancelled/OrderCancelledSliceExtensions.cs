using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.OrderCancelled;

internal static class OrderCancelledSliceExtensions
{
    public static IServiceCollection AddOrderCancelledSlice(this IServiceCollection services)
    {
        services.AddEventHandler<OrderCancelledEvent, OrderCancelledHandler>();
        return services;
    }
}
