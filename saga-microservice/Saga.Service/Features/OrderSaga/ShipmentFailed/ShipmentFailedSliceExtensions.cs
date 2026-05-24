using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.ShipmentFailed;

internal static class ShipmentFailedSliceExtensions
{
    public static IServiceCollection AddShipmentFailedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ShipmentFailedEvent, ShipmentFailedHandler>();
        return services;
    }
}
