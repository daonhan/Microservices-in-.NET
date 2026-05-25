using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.StockReleased;

internal static class StockReleasedSliceExtensions
{
    public static IServiceCollection AddStockReleasedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<StockReleasedEvent, StockReleasedHandler>();
        return services;
    }
}
