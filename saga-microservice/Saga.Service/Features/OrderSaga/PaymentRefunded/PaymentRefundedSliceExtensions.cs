using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.PaymentRefunded;

internal static class PaymentRefundedSliceExtensions
{
    public static IServiceCollection AddPaymentRefundedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<PaymentRefundedEvent, PaymentRefundedHandler>();
        return services;
    }
}
