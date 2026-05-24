using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.PaymentFailed;

internal static class PaymentFailedSliceExtensions
{
    public static IServiceCollection AddPaymentFailedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<PaymentFailedEvent, PaymentFailedHandler>();
        return services;
    }
}
