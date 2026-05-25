using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.StockReservationFailed;

internal static class StockReservationFailedSliceExtensions
{
    public static IServiceCollection AddStockReservationFailedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<StockReservationFailedEvent, StockReservationFailedHandler>();
        return services;
    }
}
