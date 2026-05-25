using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.RefundSaga.PaymentFailed;

internal static class PaymentFailedSliceExtensions
{
    public static IServiceCollection AddRefundSagaPaymentFailedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<PaymentFailedEvent, PaymentFailedHandler>();
        return services;
    }
}
