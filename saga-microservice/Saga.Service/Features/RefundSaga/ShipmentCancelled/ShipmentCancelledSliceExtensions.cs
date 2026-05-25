using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.RefundSaga.ShipmentCancelled;

internal static class ShipmentCancelledSliceExtensions
{
    public static IServiceCollection AddRefundSagaShipmentCancelledSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ShipmentCancelledEvent, ShipmentCancelledHandler>();
        return services;
    }
}
