using ECommerce.Shared.Infrastructure.EventBus;

namespace Payment.Service.Features.CapturePaymentCommand;

internal static class CapturePaymentCommandSliceExtensions
{
    public static IServiceCollection AddCapturePaymentCommandSlice(this IServiceCollection services)
    {
        services.AddEventHandler<ECommerce.Shared.IntegrationEvents.Commands.CapturePaymentCommand, CapturePaymentCommandHandler>();
        return services;
    }
}
