using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.RefundSaga.PaymentRefunded;

internal static class PaymentRefundedSliceExtensions
{
    public static IServiceCollection AddRefundSagaPaymentRefundedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<PaymentRefundedEvent, PaymentRefundedHandler>();
        return services;
    }
}
