using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.StockReserved;

internal static class StockReservedSliceExtensions
{
    public static IServiceCollection AddStockReservedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<StockReservedEvent, StockReservedHandler>();
        return services;
    }
}
