using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.Features.RefundPaymentCommand;

internal static class RefundPaymentCommandSliceExtensions
{
    public static IServiceCollection AddRefundPaymentCommandSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ECommerce.Shared.IntegrationEvents.Commands.RefundPaymentCommand, RefundPaymentCommandHandler>();
        return services;
    }
}
