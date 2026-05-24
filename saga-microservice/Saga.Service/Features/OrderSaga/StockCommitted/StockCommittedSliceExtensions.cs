using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.StockCommitted;

internal static class StockCommittedSliceExtensions
{
    public static IServiceCollection AddStockCommittedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<StockCommittedEvent, StockCommittedHandler>();
        return services;
    }
}
