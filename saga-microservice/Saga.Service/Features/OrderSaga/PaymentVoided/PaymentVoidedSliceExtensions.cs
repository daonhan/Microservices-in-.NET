using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.PaymentVoided;

internal static class PaymentVoidedSliceExtensions
{
    public static IServiceCollection AddPaymentVoidedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<PaymentVoidedEvent, PaymentVoidedHandler>();
        return services;
    }
}
