using ECommerce.Shared.Infrastructure.EventBus;
using Saga.Service.Contracts.Integration.InboundEvents;

namespace Saga.Service.Features.OrderSaga.PaymentAuthorized;

internal static class PaymentAuthorizedSliceExtensions
{
    public static IServiceCollection AddPaymentAuthorizedSlice(this IServiceCollection services)
    {
        services.AddEventHandler<PaymentAuthorizedEvent, PaymentAuthorizedHandler>();
        return services;
    }
}
