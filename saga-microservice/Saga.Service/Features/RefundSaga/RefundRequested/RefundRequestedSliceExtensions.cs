using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.RefundSaga.RefundRequested;

internal static class RefundRequestedSliceExtensions
{
    public static IServiceCollection AddRefundRequestedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<RefundRequestedEvent, RefundRequestedHandler>();
        return services;
    }
}
