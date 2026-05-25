using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.RefundSaga.ShipmentFailed;

internal static class ShipmentFailedSliceExtensions
{
    public static IServiceCollection AddRefundSagaShipmentFailedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ShipmentFailedEvent, ShipmentFailedHandler>();
        return services;
    }
}
