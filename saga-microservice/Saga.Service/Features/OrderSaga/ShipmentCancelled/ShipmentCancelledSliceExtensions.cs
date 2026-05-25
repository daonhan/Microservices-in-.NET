using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.ShipmentCancelled;

internal static class ShipmentCancelledSliceExtensions
{
    public static IServiceCollection AddShipmentCancelledSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ShipmentCancelledEvent, ShipmentCancelledHandler>();
        return services;
    }
}
