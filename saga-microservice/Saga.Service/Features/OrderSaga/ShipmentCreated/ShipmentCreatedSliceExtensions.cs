using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.ShipmentCreated;

internal static class ShipmentCreatedSliceExtensions
{
    public static IServiceCollection AddShipmentCreatedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ShipmentCreatedEvent, ShipmentCreatedHandler>();
        return services;
    }
}
