using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.OrderConfirmed;

internal static class OrderConfirmedSliceExtensions
{
    public static IServiceCollection AddOrderConfirmedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<OrderConfirmedEvent, OrderConfirmedHandler>();
        return services;
    }
}
