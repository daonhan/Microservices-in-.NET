using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.RefundSaga.OrderCancelled;

internal static class OrderCancelledSliceExtensions
{
    public static IServiceCollection AddRefundSagaOrderCancelledSlice(this IServiceCollection services)
    {
        services.AddEventHandler<OrderCancelledEvent, OrderCancelledHandler>();
        return services;
    }
}
